use std::{
    ffi::c_void,
    sync::{
        atomic::{AtomicU8, Ordering},
        mpsc::{self, Receiver, Sender},
        Mutex,
    },
    thread,
    time::Duration,
};

use serde::Serialize;
use base64::{engine::general_purpose::STANDARD, Engine as _};
use tauri::{
    menu::{CheckMenuItem, Menu, MenuItem},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
    Emitter, Manager,
};
use tauri_plugin_autostart::ManagerExt as _;
use windows::{
    core::{BSTR, Result as WinResult, VARIANT},
    Media::Control::{
        GlobalSystemMediaTransportControlsSession,
        GlobalSystemMediaTransportControlsSessionManager,
        GlobalSystemMediaTransportControlsSessionMediaProperties,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus,
    },
    Storage::Streams::DataReader,
    Win32::{
        Foundation::{FILETIME, HMODULE, HWND, TRUE},
        Graphics::Gdi::{CreateRoundRectRgn, DeleteObject, SetWindowRgn},
        Media::Audio::{eConsole, eRender, IMMDeviceEnumerator, MMDeviceEnumerator},
        Media::Audio::Endpoints::IAudioEndpointVolume,
        System::{
            Com::{CoCreateInstance, CoInitializeEx, CLSCTX_INPROC_SERVER, COINIT_MULTITHREADED},
            LibraryLoader::{GetProcAddress, LoadLibraryW},
            Power::{GetSystemPowerStatus, SYSTEM_POWER_STATUS},
            SystemInformation::{GlobalMemoryStatusEx, MEMORYSTATUSEX},
            Threading::GetSystemTimes,
            Wmi::{ISWbemLocator, ISWbemServices, SWbemLocator},
        },
    },
};

const SURFACE_COLLAPSED: u8 = 0;
const SURFACE_EXPANDED: u8 = 1;
const WINDOW_LOGICAL_WIDTH: f64 = 396.0;
const SURFACE_TOP: f64 = 26.0;
const OPTICAL_OFFSET_X: f64 = -1.0;
const REGION_MARGIN: f64 = 4.0;

#[derive(Clone, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct SystemSnapshot {
    pub cpu_percent: f64,
    pub memory_percent: f64,
    pub battery_percent: Option<f64>,
    pub gpu_percent: f64,
    pub cpu_temp_c: Option<f64>,
    pub gpu_temp_c: Option<f64>,
    pub brightness_percent: Option<f64>,
    pub volume_percent: f64,
}

impl Default for SystemSnapshot {
    fn default() -> Self {
        Self {
            cpu_percent: 0.0,
            memory_percent: 0.0,
            battery_percent: None,
            gpu_percent: 0.0,
            cpu_temp_c: None,
            gpu_temp_c: None,
            brightness_percent: None,
            volume_percent: 0.0,
        }
    }
}

#[derive(Clone, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct MediaSnapshot {
    pub playing: bool,
    pub title: String,
    pub artist: String,
    pub position_seconds: f64,
    pub duration_seconds: f64,
    pub art_data_uri: Option<String>,
    pub has_media: bool,
}

impl Default for MediaSnapshot {
    fn default() -> Self {
        Self {
            playing: false,
            title: "未在播放".into(),
            artist: "打开任意音乐应用".into(),
            position_seconds: 0.0,
            duration_seconds: 0.0,
            art_data_uri: None,
            has_media: false,
        }
    }
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct NotificationPayload {
    pub id: u64,
    pub title: String,
    pub body: String,
    pub time: u64,
}

#[derive(Clone, Copy, Default)]
struct CpuTimes {
    idle: u64,
    kernel: u64,
    user: u64,
}

enum MediaCommand {
    Toggle,
    Next,
    Previous,
}

pub struct AppState {
    surface: AtomicU8,
    cpu_times: Mutex<Option<CpuTimes>>,
    media: Mutex<MediaSnapshot>,
    media_tx: Mutex<Sender<MediaCommand>>,
}

impl AppState {
    fn new(media_tx: Sender<MediaCommand>) -> Self {
        Self {
            surface: AtomicU8::new(SURFACE_COLLAPSED),
            cpu_times: Mutex::new(None),
            media: Mutex::new(MediaSnapshot::default()),
            media_tx: Mutex::new(media_tx),
        }
    }
}

#[tauri::command]
fn system_snapshot(state: tauri::State<'_, AppState>) -> SystemSnapshot {
    read_system_snapshot(&state)
}

#[tauri::command]
fn media_snapshot(state: tauri::State<'_, AppState>) -> MediaSnapshot {
    state.media.lock().unwrap().clone()
}

#[tauri::command]
fn media_action(state: tauri::State<'_, AppState>, action: String) -> Result<(), String> {
    let command = match action.as_str() {
        "toggle" => MediaCommand::Toggle,
        "next" => MediaCommand::Next,
        "previous" => MediaCommand::Previous,
        _ => return Err(format!("未知媒体操作: {action}")),
    };
    state
        .media_tx
        .lock()
        .unwrap()
        .send(command)
        .map_err(|error| error.to_string())
}

#[tauri::command]
fn surface_state_changed(app_state: tauri::State<'_, AppState>, state: String) {
    let value = if state == "expanded" {
        SURFACE_EXPANDED
    } else {
        SURFACE_COLLAPSED
    };
    app_state.surface.store(value, Ordering::Relaxed);
}

#[tauri::command]
fn set_volume(percent: f64) -> Result<(), String> {
    set_volume_percent(percent).map_err(|error| error.to_string())
}

#[tauri::command]
fn set_brightness(percent: f64) -> Result<(), String> {
    set_brightness_percent(percent).map_err(|error| error.to_string())
}

fn island_region_bounds(width: f64, height: f64, radius: f64) -> (f64, f64, f64, f64, f64) {
    let x = (WINDOW_LOGICAL_WIDTH - width) / 2.0 + OPTICAL_OFFSET_X - REGION_MARGIN;
    let y = SURFACE_TOP - REGION_MARGIN;
    let region_width = width + REGION_MARGIN * 2.0;
    let region_height = height + REGION_MARGIN * 2.0;
    (x, y, region_width, region_height, radius + REGION_MARGIN)
}

fn apply_island_hit_region<R: tauri::Runtime>(
    window: &tauri::WebviewWindow<R>,
    x: f64,
    y: f64,
    width: f64,
    height: f64,
    radius: f64,
) -> Result<(), String> {
    let scale = window.scale_factor().map_err(|error| error.to_string())?;
    let left = (x * scale).round() as i32;
    let top = (y * scale).round() as i32;
    let right = left + ((width * scale).round() as i32).max(1);
    let bottom = top + ((height * scale).round() as i32).max(1);
    let corner = ((radius * 2.0 * scale).round() as i32)
        .clamp(0, (right - left).min(bottom - top));
    let hwnd = window.hwnd().map_err(|error| error.to_string())?;

    unsafe {
        let region = CreateRoundRectRgn(left, top, right, bottom, corner, corner);
        if region.is_invalid() {
            return Err("无法创建灵动岛窗口区域".into());
        }
        if SetWindowRgn(HWND(hwnd.0), region, TRUE) == 0 {
            let _ = DeleteObject(region);
            return Err("无法应用灵动岛窗口区域".into());
        }
    }
    Ok(())
}

#[tauri::command]
fn set_island_hit_region(
    app: tauri::AppHandle,
    x: f64,
    y: f64,
    width: f64,
    height: f64,
    radius: f64,
) -> Result<(), String> {
    let Some(window) = app.get_webview_window("main") else {
        return Ok(());
    };
    apply_island_hit_region(&window, x, y, width, height, radius)
}

#[tauri::command]
fn notify(app: tauri::AppHandle, title: String, body: String) -> Result<(), String> {
    let payload = NotificationPayload {
        id: unix_millis(),
        title,
        body,
        time: unix_millis(),
    };
    app.emit("notification", &payload)
        .map_err(|error| error.to_string())
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let (media_tx, media_rx) = mpsc::channel();

    tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
            show_main_window(app);
        }))
        .plugin(tauri_plugin_autostart::init(
            tauri_plugin_autostart::MacosLauncher::LaunchAgent,
            Some(vec![]),
        ))
        .manage(AppState::new(media_tx))
        .setup(|app| {
            setup_tray(app)?;
            #[cfg(not(debug_assertions))]
            enable_autostart_on_first_run(app);
            start_media_worker(app.handle().clone(), media_rx);
            start_system_worker(app.handle().clone());
            position_main_window(app)?;
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            system_snapshot,
            media_snapshot,
            media_action,
            surface_state_changed,
            set_volume,
            set_brightness,
            set_island_hit_region,
            notify
        ])
        .run(tauri::generate_context!())
        .expect("error while running Dynamic Island");
}

fn show_main_window(app: &tauri::AppHandle) {
    if let Some(window) = app.get_webview_window("main") {
        let _ = window.show();
        let _ = window.set_focus();
    }
}

fn toggle_autostart(app: &tauri::AppHandle) -> bool {
    let manager = app.autolaunch();
    if manager.is_enabled().unwrap_or(false) {
        let _ = manager.disable();
    } else {
        let _ = manager.enable();
    }
    manager.is_enabled().unwrap_or(false)
}

#[cfg(not(debug_assertions))]
fn enable_autostart_on_first_run(app: &tauri::App) {
    let manager = app.autolaunch();
    if manager.is_enabled().unwrap_or(false) {
        let _ = manager.enable();
        return;
    }
    let marker = app
        .path()
        .app_config_dir()
        .ok()
        .map(|dir| dir.join("autostart.initialized"));
    if let Some(path) = marker.as_ref() {
        if path.exists() {
            return;
        }
    }
    if manager.enable().is_ok() {
        if let Some(path) = marker {
            if let Some(parent) = path.parent() {
                let _ = std::fs::create_dir_all(parent);
            }
            let _ = std::fs::write(path, b"1");
        }
    }
}

fn setup_tray(app: &tauri::App) -> tauri::Result<()> {
    let show = MenuItem::with_id(app, "show", "显示灵动岛", true, None::<&str>)?;
    let autostart_enabled = app.autolaunch().is_enabled().unwrap_or(false);
    let autostart = CheckMenuItem::with_id(
        app,
        "autostart",
        "开机自启",
        true,
        autostart_enabled,
        None::<&str>,
    )?;
    let quit = MenuItem::with_id(app, "quit", "退出", true, None::<&str>)?;
    let menu = Menu::with_items(app, &[&show, &autostart, &quit])?;
    let autostart_item = autostart.clone();

    let mut builder = TrayIconBuilder::with_id("main-tray")
        .tooltip("Dynamic Island")
        .menu(&menu)
        .show_menu_on_left_click(false)
        .on_menu_event(move |app, event| match event.id.as_ref() {
            "show" => show_main_window(app),
            "autostart" => {
                let enabled = toggle_autostart(app);
                let _ = autostart_item.set_checked(enabled);
            }
            "quit" => app.exit(0),
            _ => {}
        })
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                show_main_window(tray.app_handle());
            }
        });

    if let Some(icon) = app.default_window_icon().cloned() {
        builder = builder.icon(icon);
    }
    builder.build(app)?;
    Ok(())
}
fn position_main_window(app: &tauri::App) -> tauri::Result<()> {
    let Some(window) = app.get_webview_window("main") else {
        return Ok(());
    };
    let _ = window.set_decorations(false);
    let _ = window.set_size(tauri::LogicalSize::new(WINDOW_LOGICAL_WIDTH, 254.0));

    if let Ok(Some(monitor)) = window.current_monitor() {
        let monitor_size = monitor.size();
        let monitor_position = monitor.position();
        let window_size = window.outer_size()?;
        let x = monitor_position.x + (monitor_size.width as i32 - window_size.width as i32) / 2;
        let y = monitor_position.y;
        let _ = window.set_position(tauri::PhysicalPosition::new(x, y));
    }

    let (region_x, region_y, region_width, region_height, region_radius) =
        island_region_bounds(176.0, 48.0, 24.0);
    let _ = apply_island_hit_region(
        &window,
        region_x,
        region_y,
        region_width,
        region_height,
        region_radius,
    );
    let _ = window.show();
    Ok(())
}

fn start_media_worker(app: tauri::AppHandle, receiver: Receiver<MediaCommand>) {
    thread::spawn(move || {
        let _ = unsafe { CoInitializeEx(None, COINIT_MULTITHREADED) };
        let mut tick: u32 = 0;

        loop {
            while let Ok(command) = receiver.try_recv() {
                execute_media_command(&command);
            }

            let expanded =
                app.state::<AppState>().surface.load(Ordering::Relaxed) == SURFACE_EXPANDED;
            let poll_interval = if expanded { 4 } else { 6 };

            if tick % poll_interval == 0 {
                if let Ok(snapshot) = read_media_snapshot() {
                    let state = app.state::<AppState>();
                    let mut cached = state.media.lock().unwrap();
                    let track_changed = cached.title != snapshot.title || cached.artist != snapshot.artist;
                    let mut snapshot = snapshot;
                    if track_changed || (snapshot.has_media && snapshot.art_data_uri.is_none()) {
                        snapshot.art_data_uri = read_media_thumbnail();
                    } else {
                        snapshot.art_data_uri = cached.art_data_uri.clone();
                    }
                    if *cached != snapshot {
                        let _ = app.emit("media-changed", &snapshot);
                        *cached = snapshot;
                    }
                }
            }

            tick = tick.wrapping_add(1);
            thread::sleep(Duration::from_millis(500));
        }
    });
}

fn start_system_worker(app: tauri::AppHandle) {
    thread::spawn(move || {
        let _ = unsafe { CoInitializeEx(None, COINIT_MULTITHREADED) };
        let mut last: Option<SystemSnapshot> = None;

        loop {
            let expanded =
                app.state::<AppState>().surface.load(Ordering::Relaxed) == SURFACE_EXPANDED;

            if expanded {
                let state = app.state::<AppState>();
                let snapshot = read_system_snapshot(&state);
                if last.as_ref() != Some(&snapshot) {
                    let _ = app.emit("system-changed", &snapshot);
                    last = Some(snapshot);
                }
                thread::sleep(Duration::from_millis(2000));
            } else {
                last = None;
                thread::sleep(Duration::from_millis(1000));
            }
        }
    });
}

fn execute_media_command(command: &MediaCommand) {
    let Ok(manager) = media_manager() else {
        return;
    };
    let Ok(sessions) = manager.GetSessions() else {
        return;
    };
    if sessions.Size().map(|size| size == 0).unwrap_or(true) {
        return;
    }
    let Ok(session) = sessions.GetAt(0) else {
        return;
    };

    match command {
        MediaCommand::Toggle => {
            let _ = if is_playing(&session) {
                session
                    .TryPauseAsync()
                    .and_then(|operation| operation.get())
            } else {
                session.TryPlayAsync().and_then(|operation| operation.get())
            };
        }
        MediaCommand::Next => {
            let _ = session
                .TrySkipNextAsync()
                .and_then(|operation| operation.get());
        }
        MediaCommand::Previous => {
            let _ = session
                .TrySkipPreviousAsync()
                .and_then(|operation| operation.get());
        }
    }
}

fn media_manager() -> WinResult<GlobalSystemMediaTransportControlsSessionManager> {
    let operation = GlobalSystemMediaTransportControlsSessionManager::RequestAsync()?;
    operation.get()
}

fn read_media_snapshot() -> WinResult<MediaSnapshot> {
    let manager = media_manager()?;
    let sessions = manager.GetSessions()?;
    if sessions.Size()? == 0 {
        return Ok(MediaSnapshot::default());
    }

    let session = sessions.GetAt(0)?;
    let playing = is_playing(&session);
    let mut snapshot = MediaSnapshot {
        playing,
        has_media: true,
        ..MediaSnapshot::default()
    };

    let properties = session.TryGetMediaPropertiesAsync()?;
    let properties = properties.get()?;
    snapshot.title = properties.Title()?.to_string();
    snapshot.artist = properties.Artist()?.to_string();

    if let Ok(timeline) = session.GetTimelineProperties() {
        if let Ok(position) = timeline.Position() {
            snapshot.position_seconds = position.Duration as f64 / 10_000_000.0;
        }
        if let Ok(end) = timeline.EndTime() {
            snapshot.duration_seconds = end.Duration as f64 / 10_000_000.0;
        }
    }

    Ok(snapshot)
}

fn read_media_thumbnail() -> Option<String> {
    let manager = media_manager().ok()?;
    let sessions = manager.GetSessions().ok()?;
    if sessions.Size().ok()? == 0 {
        return None;
    }
    let session = sessions.GetAt(0).ok()?;
    let properties = session.TryGetMediaPropertiesAsync().ok()?.get().ok()?;
    read_thumbnail_data_uri(&properties)
}

fn read_thumbnail_data_uri(
    properties: &GlobalSystemMediaTransportControlsSessionMediaProperties,
) -> Option<String> {
    let thumbnail = properties.Thumbnail().ok()?;
    let stream = thumbnail.OpenReadAsync().ok()?.get().ok()?;
    let size = stream.Size().ok()?;
    if size == 0 || size > 8 * 1024 * 1024 {
        return None;
    }
    let reader = DataReader::CreateDataReader(&stream).ok()?;
    let loaded = reader.LoadAsync(size as u32).ok()?.get().ok()?;
    if loaded < size as u32 {
        return None;
    }
    let mut buffer = vec![0u8; size as usize];
    reader.ReadBytes(&mut buffer).ok()?;
    let mime = stream
        .ContentType()
        .map(|value| value.to_string())
        .unwrap_or_else(|_| "image/jpeg".to_string());
    Some(format!("data:{};base64,{}", mime, STANDARD.encode(&buffer)))
}

fn is_playing(session: &GlobalSystemMediaTransportControlsSession) -> bool {
    let Ok(playback) = session.GetPlaybackInfo() else {
        return false;
    };
    let Ok(status) = playback.PlaybackStatus() else {
        return false;
    };
    matches!(
        status,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus::Playing
    )
}

fn read_system_snapshot(state: &AppState) -> SystemSnapshot {
    let (gpu_percent, gpu_temp_c) = nvml_gpu()
        .map(|(utilization, temperature)| (utilization, Some(temperature)))
        .unwrap_or((0.0, None));
    SystemSnapshot {
        cpu_percent: cpu_percent(state),
        memory_percent: memory_percent(),
        battery_percent: battery_percent(),
        gpu_percent,
        cpu_temp_c: cpu_temperature_c(),
        gpu_temp_c,
        brightness_percent: brightness_percent(),
        volume_percent: volume_percent().unwrap_or(0.0),
    }
}

fn com_ensure() {
    unsafe {
        let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
    }
}

fn wmi_services(namespace: &str) -> WinResult<ISWbemServices> {
    com_ensure();
    unsafe {
        let locator: ISWbemLocator = CoCreateInstance(&SWbemLocator, None, CLSCTX_INPROC_SERVER)?;
        locator.ConnectServer(
            &BSTR::from("."),
            &BSTR::from(namespace),
            &BSTR::from(""),
            &BSTR::from(""),
            &BSTR::from(""),
            &BSTR::from(""),
            0,
            None,
        )
    }
}

fn wmi_number(namespace: &str, query: &str, property: &str) -> Option<f64> {
    unsafe {
        let services = wmi_services(namespace).ok()?;
        let set = services
            .ExecQuery(&BSTR::from(query), &BSTR::from("WQL"), 0, None)
            .ok()?;
        if set.Count().ok()? == 0 {
            return None;
        }
        let object = set.ItemIndex(0).ok()?;
        let value = object
            .Properties_()
            .ok()?
            .Item(&BSTR::from(property), 0)
            .ok()?
            .Value()
            .ok()?;
        f64::try_from(&value).ok()
    }
}

fn cpu_temperature_c() -> Option<f64> {
    let tenths_kelvin = wmi_number(
        r"root\cimv2",
        "SELECT HighPrecisionTemperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation",
        "HighPrecisionTemperature",
    )?;
    Some(tenths_kelvin / 10.0 - 273.15)
}

fn brightness_percent() -> Option<f64> {
    wmi_number(
        r"root\WMI",
        "SELECT CurrentBrightness FROM WmiMonitorBrightness",
        "CurrentBrightness",
    )
}

fn set_brightness_percent(percent: f64) -> WinResult<()> {
    com_ensure();
    unsafe {
        let services = wmi_services(r"root\WMI")?;
        let set = services.ExecQuery(
            &BSTR::from("SELECT * FROM WmiMonitorBrightnessMethods"),
            &BSTR::from("WQL"),
            0,
            None,
        )?;
        let object = set.ItemIndex(0)?;
        let object_path = object.Path_()?.Path()?;
        let method = object.Methods_()?.Item(&BSTR::from("WmiSetBrightness"), 0)?;
        let instance = method.InParameters()?.SpawnInstance_(0)?;
        let properties = instance.Properties_()?;
        let timeout = VARIANT::from(0u32);
        let brightness = VARIANT::from(percent.round().clamp(0.0, 100.0) as u8);
        properties.Item(&BSTR::from("Timeout"), 0)?.SetValue(&timeout)?;
        properties.Item(&BSTR::from("Brightness"), 0)?.SetValue(&brightness)?;
        services.ExecMethod(&object_path, &BSTR::from("WmiSetBrightness"), &instance, 0, None)?;
        Ok(())
    }
}

fn volume_percent() -> Option<f64> {
    com_ensure();
    unsafe {
        let enumerator: IMMDeviceEnumerator =
            CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_INPROC_SERVER).ok()?;
        let device = enumerator.GetDefaultAudioEndpoint(eRender, eConsole).ok()?;
        let volume: IAudioEndpointVolume = device.Activate(CLSCTX_INPROC_SERVER, None).ok()?;
        volume
            .GetMasterVolumeLevelScalar()
            .ok()
            .map(|level| level as f64 * 100.0)
    }
}

fn set_volume_percent(percent: f64) -> WinResult<()> {
    com_ensure();
    unsafe {
        let enumerator: IMMDeviceEnumerator =
            CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_INPROC_SERVER)?;
        let device = enumerator.GetDefaultAudioEndpoint(eRender, eConsole)?;
        let volume: IAudioEndpointVolume = device.Activate(CLSCTX_INPROC_SERVER, None)?;
        volume.SetMasterVolumeLevelScalar(
            (percent / 100.0).clamp(0.0, 1.0) as f32,
            std::ptr::null(),
        )
    }
}

#[repr(C)]
struct NvmlUtilization {
    gpu: u32,
    memory: u32,
}

unsafe fn nvml_addr(module: HMODULE, name: &[u8]) -> Option<unsafe extern "system" fn() -> isize> {
    GetProcAddress(module, windows::core::PCSTR(name.as_ptr()))
}

fn nvml_gpu() -> Option<(f64, f64)> {
    unsafe {
        let name: Vec<u16> = "nvml.dll".encode_utf16().chain(std::iter::once(0)).collect();
        let module = LoadLibraryW(windows::core::PCWSTR(name.as_ptr())).ok()?;
        let init: unsafe extern "system" fn() -> i32 = std::mem::transmute(nvml_addr(module, b"nvmlInit_v2\0")?);
        let shutdown: unsafe extern "system" fn() -> i32 = std::mem::transmute(nvml_addr(module, b"nvmlShutdown\0")?);
        let get_handle: unsafe extern "system" fn(u32, *mut *mut c_void) -> i32 =
            std::mem::transmute(nvml_addr(module, b"nvmlDeviceGetHandleByIndex_v2\0")?);
        let get_utilization: unsafe extern "system" fn(*mut c_void, *mut NvmlUtilization) -> i32 =
            std::mem::transmute(nvml_addr(module, b"nvmlDeviceGetUtilizationRates\0")?);
        let get_temperature: unsafe extern "system" fn(*mut c_void, i32, *mut u32) -> i32 =
            std::mem::transmute(nvml_addr(module, b"nvmlDeviceGetTemperature\0")?);

        if init() != 0 {
            return None;
        }
        let mut device: *mut c_void = std::ptr::null_mut();
        if get_handle(0, &mut device) != 0 {
            shutdown();
            return None;
        }
        let mut utilization = NvmlUtilization { gpu: 0, memory: 0 };
        let _ = get_utilization(device, &mut utilization);
        let mut temperature: u32 = 0;
        let _ = get_temperature(device, 0, &mut temperature);
        shutdown();
        Some((utilization.gpu as f64, temperature as f64))
    }
}

fn memory_percent() -> f64 {
    unsafe {
        let mut memory = MEMORYSTATUSEX::default();
        memory.dwLength = std::mem::size_of::<MEMORYSTATUSEX>() as u32;
        if GlobalMemoryStatusEx(&mut memory).is_ok() {
            memory.dwMemoryLoad as f64
        } else {
            0.0
        }
    }
}

fn battery_percent() -> Option<f64> {
    unsafe {
        let mut power = SYSTEM_POWER_STATUS::default();
        if GetSystemPowerStatus(&mut power).is_err() {
            return None;
        }
        if power.BatteryLifePercent == 255 {
            None
        } else {
            Some(power.BatteryLifePercent as f64)
        }
    }
}

fn cpu_percent(state: &AppState) -> f64 {
    unsafe {
        let mut idle = FILETIME::default();
        let mut kernel = FILETIME::default();
        let mut user = FILETIME::default();
        if GetSystemTimes(Some(&mut idle), Some(&mut kernel), Some(&mut user)).is_err() {
            return 0.0;
        }

        let idle = filetime_to_u64(idle);
        let kernel = filetime_to_u64(kernel);
        let user = filetime_to_u64(user);
        let mut previous = state.cpu_times.lock().unwrap();

        let Some(prev) = *previous else {
            *previous = Some(CpuTimes { idle, kernel, user });
            return 0.0;
        };

        let idle_delta = idle.saturating_sub(prev.idle);
        let kernel_delta = kernel.saturating_sub(prev.kernel);
        let user_delta = user.saturating_sub(prev.user);
        *previous = Some(CpuTimes { idle, kernel, user });

        let total = kernel_delta + user_delta;
        if total == 0 {
            return 0.0;
        }
        let busy = total.saturating_sub(idle_delta);
        (busy as f64 / total as f64) * 100.0
    }
}

fn filetime_to_u64(filetime: FILETIME) -> u64 {
    ((filetime.dwHighDateTime as u64) << 32) | filetime.dwLowDateTime as u64
}


fn unix_millis() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|duration| duration.as_millis() as u64)
        .unwrap_or(0)
}
