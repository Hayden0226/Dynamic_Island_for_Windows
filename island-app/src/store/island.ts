import { reactive } from "vue";
import { listen, type UnlistenFn } from "@tauri-apps/api/event";
import { isTauri } from "@tauri-apps/api/core";
import type { IslandMode } from "@/core/metrics";
import { frameForMode } from "@/core/animator";
import { getSystemSnapshot, getMediaSnapshot, type MediaSnapshot, type SystemSnapshot } from "@/core/ipc";

export interface NotificationItem {
  id: number;
  title: string;
  body: string;
  time: number;
}

interface State {
  mode: IslandMode;
  pinned: boolean;
  reduceMotion: boolean;
  media: MediaSnapshot;
  system: SystemSnapshot;
  notifications: NotificationItem[];
}

export const state = reactive<State>({
  mode: "collapsed",
  pinned: false,
  reduceMotion: false,
  media: { playing: false, title: "未在播放", artist: "打开任意音乐应用", positionSeconds: 0, durationSeconds: 0, artDataUri: null, hasMedia: false },
  system: { cpuPercent: 0, memoryPercent: 0, batteryPercent: null, gpuPercent: 0, cpuTempC: null, gpuTempC: null, brightnessPercent: null, volumePercent: 0 },
  notifications: [],
});

let unlisteners: UnlistenFn[] = [];
let mediaTimer: number | undefined;
let systemTimer: number | undefined;

export function requestMode(mode: IslandMode): void {
  state.mode = mode;
}

export function togglePinned(): void {
  state.pinned = !state.pinned;
  if (state.pinned) state.mode = "expanded";
}

export async function initStore(): Promise<void> {
  await refreshMedia();
  await refreshSystem();

  if (isTauri()) {
    unlisteners.push(await listen<MediaSnapshot>("media-changed", (event) => (state.media = event.payload)));
    unlisteners.push(await listen<SystemSnapshot>("system-changed", (event) => (state.system = event.payload)));
    unlisteners.push(await listen<NotificationItem>("notification", (event) => (state.notifications = [event.payload, ...state.notifications].slice(0, 4))));
    return;
  }

  mediaTimer = window.setInterval(refreshMedia, 5000);
  systemTimer = window.setInterval(refreshSystem, 3000);
}

export function disposeStore(): void {
  for (const unlisten of unlisteners) unlisten();
  unlisteners = [];
  if (mediaTimer) window.clearInterval(mediaTimer);
  if (systemTimer) window.clearInterval(systemTimer);
}

async function refreshMedia(): Promise<void> {
  try {
    state.media = await getMediaSnapshot();
  } catch {
    /* keep the previous snapshot */
  }
}

async function refreshSystem(): Promise<void> {
  try {
    state.system = await getSystemSnapshot();
  } catch {
    /* keep the previous snapshot */
  }
}

export function targetFrame(): ReturnType<typeof frameForMode> {
  return frameForMode(state.mode);
}
