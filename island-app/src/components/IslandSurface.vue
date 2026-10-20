<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import { IslandAnimator, frameForMode } from "@/core/animator";
import { islandPath } from "@/core/squircle";
import { METRICS, type IslandFrame, type IslandMode } from "@/core/metrics";
import { state, requestMode, togglePinned } from "@/store/island";
import { mediaAction, setBrightness, setIslandHitRegion, setSurfaceState, setVolume } from "@/core/ipc";

const frame = reactive<IslandFrame>({
  width: METRICS.collapsedWidth,
  height: METRICS.collapsedHeight,
  cornerRadius: METRICS.cornerRadius,
  collapsedOpacity: 1,
  contentOpacity: 0,
  contentOffsetY: -METRICS.contentRise,
});

const timeText = ref("--:--");
let collapseTimer: number | undefined;
let regionTimer: number | undefined;
let modeToken = 0;
let regionToken = 0;
let animator: IslandAnimator | undefined;
let activeRegion: { mode: IslandMode; hasMedia: boolean } = { mode: "collapsed", hasMedia: false };

function scheduleCollapse(): void {
  window.clearTimeout(collapseTimer);
  if (state.pinned) return;
  collapseTimer = window.setTimeout(() => requestMode("collapsed"), METRICS.collapseDelay);
}

function cancelCollapse(): void {
  window.clearTimeout(collapseTimer);
}

function applyFrame(next: IslandFrame): void {
  Object.assign(frame, next);
}

async function applyRegion(mode: IslandMode, hasMedia: boolean): Promise<void> {
  activeRegion = { mode, hasMedia };
  const target = frameForMode(mode, hasMedia);
  const x = (METRICS.windowWidth - target.width) / 2 + METRICS.opticalOffsetX;
  const y = METRICS.surfaceTop;
  try {
    await setIslandHitRegion(x, y, target.width, target.height);
  } catch {
    /* keep the previous region if the native update fails */
  }
}

function cancelRegionUpdate(): void {
  window.clearTimeout(regionTimer);
  regionToken += 1;
}

function scheduleRegion(mode: IslandMode, hasMedia: boolean, delay: number): void {
  const token = ++regionToken;
  window.clearTimeout(regionTimer);
  if (delay <= 0) {
    void applyRegion(mode, hasMedia);
    return;
  }
  regionTimer = window.setTimeout(() => {
    if (token === regionToken) void applyRegion(mode, hasMedia);
  }, delay);
}

function reapplyRegion(): void {
  window.clearTimeout(regionTimer);
  regionTimer = window.setTimeout(
    () => void applyRegion(activeRegion.mode, activeRegion.hasMedia),
    60,
  );
}

function updateClock(): void {
  const now = new Date();
  timeText.value = `${String(now.getHours()).padStart(2, "0")}:${String(now.getMinutes()).padStart(2, "0")}`;
}

watch(
  () => state.mode,
  async (mode) => {
    const token = ++modeToken;
    const hasMedia = state.media.hasMedia;
    cancelRegionUpdate();
    if (mode !== "collapsed") {
      await applyRegion(mode, hasMedia);
      if (token !== modeToken || state.mode !== mode) return;
    }

    animator?.animateTo(mode, state.reduceMotion, hasMedia);
    const settle = mode === "collapsed" ? METRICS.collapseDuration : METRICS.expandDuration;
    if (mode === "collapsed") {
      scheduleRegion("collapsed", hasMedia, settle + 40);
    }
    window.setTimeout(() => setSurfaceState(mode === "collapsed" ? "collapsed" : "expanded"), settle + 260);
  },
);

watch(
  () => state.media.hasMedia,
  (hasMedia, previous) => {
    const refreshContent = state.mode === "expanded" && previous !== hasMedia;
    animator?.animateTo(state.mode, state.reduceMotion, hasMedia, refreshContent);
    if (state.mode === "collapsed") {
      scheduleRegion("collapsed", hasMedia, METRICS.collapseDuration + 40);
      return;
    }
    const previousTarget = frameForMode("expanded", previous);
    const target = frameForMode("expanded", hasMedia);
    const grows = target.width * target.height > previousTarget.width * previousTarget.height;
    scheduleRegion("expanded", hasMedia, grows ? 0 : METRICS.expandDuration + 40);
  },
);

onMounted(() => {
  animator = new IslandAnimator("collapsed", applyFrame, state.media.hasMedia);
  void applyRegion("collapsed", state.media.hasMedia);
  window.addEventListener("resize", reapplyRegion);
  updateClock();
  window.setInterval(updateClock, 15000);
});

onUnmounted(() => {
  window.clearTimeout(collapseTimer);
  window.clearTimeout(regionTimer);
  window.removeEventListener("resize", reapplyRegion);
});

const path = computed(() => islandPath(frame.width, frame.height, frame.cornerRadius, METRICS.cornerSmoothing));
const opticalShift = Math.abs(METRICS.opticalOffsetX);
const surfaceTransform = "translateX(calc(-50% - " + opticalShift + "px))";
const surfaceStyle = computed(() => ({
  width: `${frame.width}px`,
  height: `${frame.height}px`,
  transform: surfaceTransform,
}));
const surfaceClipStyle = computed(() => ({
  borderRadius: `${METRICS.cornerRadius}px`,
}));
const collapsedStyle = computed(() => ({
  opacity: frame.collapsedOpacity,
}));
const contentScale = computed(() => Math.min(1, frame.width / METRICS.expandedWidth));
const expandedStyle = computed(() => ({
  opacity: frame.contentOpacity,
  transform: `translateX(-50%) translateY(${frame.contentOffsetY}px) scale(${contentScale.value})`,
  pointerEvents: state.mode === "collapsed" ? ("none" as const) : ("auto" as const),
}));

function onMouseEnter(): void {
  cancelCollapse();
  requestMode("expanded");
}

function onMouseLeave(): void {
  scheduleCollapse();
}

function onMediaAction(action: "toggle" | "next" | "previous"): void {
  void mediaAction(action);
}

function onBrightness(event: Event): void {
  const value = Number((event.target as HTMLInputElement).value);
  state.system.brightnessPercent = value;
  void setBrightness(value);
}

function onVolume(event: Event): void {
  const value = Number((event.target as HTMLInputElement).value);
  state.system.volumePercent = value;
  void setVolume(value);
}

const progress = computed(() => {
  if (!state.media.hasMedia || state.media.durationSeconds <= 0) return 0;
  return Math.min(1, state.media.positionSeconds / state.media.durationSeconds);
});

function formatSeconds(value: number): string {
  const total = Math.max(0, Math.floor(value));
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, "0")}`;
}
</script>

<template>
  <div class="island-stage" @contextmenu.prevent>
    <div
      class="island-surface"
      :style="surfaceStyle"
      @mouseenter="onMouseEnter"
      @mouseleave="onMouseLeave"
    >
      <svg
        class="island-shell"
        :viewBox="`0 0 ${frame.width} ${frame.height}`"
        :width="frame.width"
        :height="frame.height"
      >
        <defs>
          <linearGradient id="shellGradient" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" stop-color="#1b1b1f" />
            <stop offset="1" stop-color="#101013" />
          </linearGradient>
        </defs>
        <path class="shell-fill" :d="path" stroke="url(#shellGradient)" stroke-width="2" stroke-linejoin="round" />
        <path class="shell-rim" :d="path" />
      </svg>

      <div class="surface-content" :style="surfaceClipStyle">
        <div class="collapsed-view" :style="collapsedStyle" aria-hidden="true">
          <div v-if="state.media.hasMedia" class="collapsed-art">
            <img v-if="state.media.artDataUri" :src="state.media.artDataUri" alt="" class="art-image" />
            <span v-else class="art-fallback">♪</span>
          </div>
          <div class="collapsed-time">{{ timeText }}</div>
        </div>

      <div
        class="expanded-view"
        :class="{ compact: !state.media.hasMedia }"
        :style="expandedStyle"
      >
        <div v-if="state.media.hasMedia" class="media-section">
          <div class="top-row">
            <div class="album-art">
              <img v-if="state.media.artDataUri" :src="state.media.artDataUri" alt="" class="art-image" />
              <span v-else class="art-fallback">♪</span>
            </div>
            <div class="track-meta">
              <div class="track-title">{{ state.media.title }}</div>
              <div class="track-artist">{{ state.media.artist }}</div>
              <div class="progress-row">
                <div class="progress-track">
                  <div class="progress-fill" :style="{ width: `${progress * 100}%` }"></div>
                </div>
                <span class="progress-time">{{ formatSeconds(state.media.positionSeconds) }} / {{ formatSeconds(state.media.durationSeconds) }}</span>
              </div>
            </div>
            <button class="pin-button" :title="state.pinned ? '取消保持展开' : '保持展开'" :style="{ color: state.pinned ? 'var(--island-accent)' : 'rgba(255, 255, 255, 0.7)' }" @click="togglePinned">&#xE718;</button>
          </div>

          <div class="playback-row">
            <button class="icon-button" title="上一首" @click="onMediaAction('previous')">⏮</button>
            <button class="play-button" title="播放 / 暂停" @click="onMediaAction('toggle')">{{ state.media.playing ? "⏸" : "▶" }}</button>
            <button class="icon-button" title="下一首" @click="onMediaAction('next')">⏭</button>
          </div>

          <div class="bottom-divider"></div>
        </div>

        <div class="bottom-section">
          <div class="chips">
            <div class="chip">
              <span class="chip-label">CPU</span>
              <span class="chip-value">{{ Math.round(state.system.cpuPercent) }}%</span>
              <span v-if="state.system.cpuTempC !== null" class="chip-sub">{{ Math.round(state.system.cpuTempC) }}°C</span>
            </div>
            <div class="chip">
              <span class="chip-label">RAM</span>
              <span class="chip-value">{{ Math.round(state.system.memoryPercent) }}%</span>
            </div>
            <div class="chip">
              <span class="chip-label">GPU</span>
              <span class="chip-value">{{ Math.round(state.system.gpuPercent) }}%</span>
              <span v-if="state.system.gpuTempC !== null" class="chip-sub">{{ Math.round(state.system.gpuTempC) }}°C</span>
            </div>
          </div>

          <div class="quick-controls">
            <label class="control">
              <span class="control-label">亮度</span>
              <input
                class="control-slider"
                type="range"
                min="0"
                max="100"
                step="1"
                :value="Math.round(state.system.brightnessPercent ?? 0)"
                :disabled="state.system.brightnessPercent === null"
                @input="onBrightness"
              />
              <span class="control-value">{{ state.system.brightnessPercent === null ? "—" : Math.round(state.system.brightnessPercent) + "%" }}</span>
            </label>
            <label class="control">
              <span class="control-label">音量</span>
              <input
                class="control-slider"
                type="range"
                min="0"
                max="100"
                step="1"
                :value="Math.round(state.system.volumePercent)"
                @input="onVolume"
              />
              <span class="control-value">{{ Math.round(state.system.volumePercent) }}%</span>
            </label>
          </div>
        </div>
      </div>
      </div>
    </div>

    <div v-if="state.notifications.length" class="notification-stack">
      <div v-for="item in state.notifications" :key="item.id" class="notification-item">
        <div class="notification-title">{{ item.title }}</div>
        <div class="notification-body">{{ item.body }}</div>
      </div>
    </div>
  </div>
</template>
