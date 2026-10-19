<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import { IslandAnimator } from "@/core/animator";
import { islandPath } from "@/core/squircle";
import { METRICS, type IslandFrame } from "@/core/metrics";
import { state, requestMode, togglePinned } from "@/store/island";
import { mediaAction, setBrightness, setSurfaceState, setVolume } from "@/core/ipc";

const frame = reactive<IslandFrame>({
  width: METRICS.collapsedWidth,
  height: METRICS.collapsedHeight,
  cornerRadius: METRICS.collapsedHeight / 2,
  collapsedOpacity: 1,
  contentOpacity: 0,
  contentOffsetY: 0,
  controlsOpacity: 0,
  bottomOpacity: 0,
  bottomOffsetY: 0,
});

const timeText = ref("--:--");
let collapseTimer: number | undefined;
let animator: IslandAnimator | undefined;

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

function updateClock(): void {
  const now = new Date();
  timeText.value = `${String(now.getHours()).padStart(2, "0")}:${String(now.getMinutes()).padStart(2, "0")}`;
}

watch(
  () => state.mode,
  (mode) => {
    animator?.animateTo(mode, state.reduceMotion);
    const settle = mode === "collapsed" ? METRICS.collapseDuration : METRICS.expandDuration;
    window.setTimeout(() => setSurfaceState(mode === "collapsed" ? "collapsed" : "expanded"), settle + 260);
  },
);

onMounted(() => {
  animator = new IslandAnimator("collapsed", applyFrame);
  updateClock();
  window.setInterval(updateClock, 15000);
});

onUnmounted(() => {
  window.clearTimeout(collapseTimer);
});

const path = computed(() => islandPath(frame.width, frame.height, frame.cornerRadius, METRICS.cornerSmoothing));
const opticalShift = Math.abs(METRICS.opticalOffsetX);
const shellTransform = "translateX(calc(-50% - " + opticalShift + "px))";
const svgStyle = computed(() => ({
  width: `${frame.width}px`,
  height: `${frame.height}px`,
  transition: "none",
  transform: shellTransform,
}));
const collapsedStyle = computed(() => ({
  width: `${frame.width}px`,
  opacity: frame.collapsedOpacity,
  transform: shellTransform,
}));
const expandedStyle = computed(() => ({
  opacity: frame.contentOpacity,
  transform: "translateX(calc(-50% - " + opticalShift + "px))",
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
  <div class="island-stage" @mouseenter="onMouseEnter" @mouseleave="onMouseLeave" @contextmenu.prevent>
    <svg
      class="island-shell"
      :style="svgStyle"
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
      <path class="shell-fill" :d="path" />
      <path class="shell-rim" :d="path" />
    </svg>

    <div class="collapsed-view" :style="collapsedStyle" aria-hidden="true">
      <div class="collapsed-art">
        <img v-if="state.media.artDataUri" :src="state.media.artDataUri" alt="" class="art-image" />
        <span v-else class="art-fallback">♪</span>
      </div>
      <div class="collapsed-time">{{ timeText }}</div>
    </div>

    <div class="expanded-view" :style="expandedStyle">
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

      <div class="playback-row" :style="{ opacity: frame.controlsOpacity }">
        <button class="icon-button" title="上一首" @click="onMediaAction('previous')">⏮</button>
        <button class="play-button" title="播放 / 暂停" @click="onMediaAction('toggle')">{{ state.media.playing ? "⏸" : "▶" }}</button>
        <button class="icon-button" title="下一首" @click="onMediaAction('next')">⏭</button>
      </div>

      <div class="bottom-divider" :style="{ opacity: frame.bottomOpacity }"></div>

      <div class="bottom-section" :style="{ opacity: frame.bottomOpacity, transform: `translateY(${frame.bottomOffsetY}px)` }">
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
    <div v-if="state.notifications.length" class="notification-stack">
      <div v-for="item in state.notifications" :key="item.id" class="notification-item">
        <div class="notification-title">{{ item.title }}</div>
        <div class="notification-body">{{ item.body }}</div>
      </div>
    </div>
  </div>
</template>
