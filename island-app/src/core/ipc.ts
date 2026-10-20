import { invoke, isTauri } from "@tauri-apps/api/core";

export interface SystemSnapshot {
  cpuPercent: number;
  memoryPercent: number;
  batteryPercent: number | null;
  gpuPercent: number;
  cpuTempC: number | null;
  gpuTempC: number | null;
  brightnessPercent: number | null;
  volumePercent: number;
}

export interface MediaSnapshot {
  playing: boolean;
  title: string;
  artist: string;
  positionSeconds: number;
  durationSeconds: number;
  artDataUri: string | null;
  hasMedia: boolean;
}

export type SurfaceState = "collapsed" | "expanded";

function mockSystem(): SystemSnapshot {
  return { cpuPercent: 8, memoryPercent: 41, batteryPercent: 76, gpuPercent: 12, cpuTempC: 52, gpuTempC: 48, brightnessPercent: 60, volumePercent: 42 };
}

function mockMedia(): MediaSnapshot {
  return { playing: false, title: "", artist: "", positionSeconds: 0, durationSeconds: 0, artDataUri: null, hasMedia: false };
}

export async function getSystemSnapshot(): Promise<SystemSnapshot> {
  if (!isTauri()) return mockSystem();
  return await invoke<SystemSnapshot>("system_snapshot");
}

export async function getMediaSnapshot(): Promise<MediaSnapshot> {
  if (!isTauri()) return mockMedia();
  return await invoke<MediaSnapshot>("media_snapshot");
}

export async function mediaAction(action: "toggle" | "next" | "previous"): Promise<void> {
  if (!isTauri()) return;
  await invoke("media_action", { action });
}

export async function setSurfaceState(state: SurfaceState): Promise<void> {
  if (!isTauri()) return;
  await invoke("surface_state_changed", { state });
}

export async function pushNotification(title: string, body: string): Promise<void> {
  if (!isTauri()) return;
  await invoke("notify", { title, body });
}

export async function setVolume(percent: number): Promise<void> {
  if (!isTauri()) return;
  await invoke("set_volume", { percent });
}

export async function setBrightness(percent: number): Promise<void> {
  if (!isTauri()) return;
  await invoke("set_brightness", { percent });
}

export async function setIslandHitRegion(
  x: number,
  y: number,
  width: number,
  height: number,
): Promise<void> {
  if (!isTauri()) return;
  await invoke("set_island_hit_region", { x, y, width, height });
}
