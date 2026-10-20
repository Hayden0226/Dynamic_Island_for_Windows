import { METRICS, type IslandFrame, type IslandMode } from "./metrics";
import { balanced, outCubic } from "./easing";

class Track {
  constructor(
    public readonly from: number,
    public readonly to: number,
    public readonly delay: number,
    public readonly duration: number,
    public readonly ease: (t: number) => number,
  ) {}

  valueAt(elapsed: number): number {
    if (elapsed <= this.delay) return this.from;
    if (this.duration <= 0) return this.to;
    const progress = Math.min(1, (elapsed - this.delay) / this.duration);
    return this.from + (this.to - this.from) * this.ease(progress);
  }
}

interface Transition {
  width: Track;
  height: Track;
  cornerRadius: Track;
  collapsedOpacity: Track;
  contentOpacity: Track;
  contentOffsetY: Track;
  controlsOpacity: Track;
  bottomOpacity: Track;
  bottomOffsetY: Track;
  total: number;
}

export function frameForMode(mode: IslandMode, hasMedia = true): IslandFrame {
  const size =
    mode === "expanded"
      ? { width: METRICS.expandedWidth, height: hasMedia ? METRICS.expandedHeight : METRICS.expandedCompactHeight }
      : mode === "peek"
        ? { width: METRICS.peekWidth, height: METRICS.peekHeight }
        : { width: hasMedia ? METRICS.collapsedWidth : METRICS.collapsedCompactWidth, height: METRICS.collapsedHeight };
  const cornerRadius = Math.min(METRICS.cornerRadius, size.height / 2);
  if (mode === "expanded") {
    return { width: size.width, height: size.height, cornerRadius, collapsedOpacity: 0, contentOpacity: 1, contentOffsetY: 0, controlsOpacity: 1, bottomOpacity: 1, bottomOffsetY: 0 };
  }
  if (mode === "peek") {
    return { width: size.width, height: size.height, cornerRadius, collapsedOpacity: 0, contentOpacity: 1, contentOffsetY: 0, controlsOpacity: 0, bottomOpacity: 0, bottomOffsetY: 0 };
  }
  return { width: size.width, height: size.height, cornerRadius, collapsedOpacity: 1, contentOpacity: 0, contentOffsetY: 0, controlsOpacity: 0, bottomOpacity: 0, bottomOffsetY: 0 };
}

export class IslandAnimator {
  private frame: IslandFrame;
  private transition: Transition | null = null;
  private startTime = 0;
  private raf = 0;
  private target: IslandMode;

  private hasMedia: boolean;

  constructor(initial: IslandMode, private readonly onFrame: (frame: IslandFrame) => void, hasMedia = true) {
    this.hasMedia = hasMedia;
    this.frame = frameForMode(initial, hasMedia);
    this.target = initial;
    this.onFrame(this.frame);
  }

  get current(): IslandFrame {
    return this.frame;
  }

  animateTo(mode: IslandMode, reduceMotion: boolean, hasMedia = this.hasMedia): void {
    if (mode === this.target && hasMedia === this.hasMedia && this.transition === null) return;
    const from = this.frame;
    const to = frameForMode(mode, hasMedia);
    this.target = mode;
    this.hasMedia = hasMedia;
    this.transition = buildTransition(from, to, mode, reduceMotion);
    this.startTime = 0;
    this.ensureRunning();
  }

  private ensureRunning(): void {
    if (this.raf) return;
    const tick = (now: number) => {
      const transition = this.transition;
      if (!transition) {
        this.raf = 0;
        return;
      }
      if (!this.startTime) this.startTime = now;
      const elapsed = now - this.startTime;
      if (elapsed >= transition.total) {
        this.frame = frameForMode(this.target, this.hasMedia);
        this.transition = null;
        this.raf = 0;
        this.onFrame(this.frame);
        return;
      }
      this.frame = {
        width: transition.width.valueAt(elapsed),
        height: transition.height.valueAt(elapsed),
        cornerRadius: transition.cornerRadius.valueAt(elapsed),
        collapsedOpacity: transition.collapsedOpacity.valueAt(elapsed),
        contentOpacity: transition.contentOpacity.valueAt(elapsed),
        contentOffsetY: transition.contentOffsetY.valueAt(elapsed),
        controlsOpacity: transition.controlsOpacity.valueAt(elapsed),
        bottomOpacity: transition.bottomOpacity.valueAt(elapsed),
        bottomOffsetY: transition.bottomOffsetY.valueAt(elapsed),
      };
      this.onFrame(this.frame);
      this.raf = requestAnimationFrame(tick);
    };
    this.raf = requestAnimationFrame(tick);
  }
}

function buildTransition(from: IslandFrame, to: IslandFrame, mode: IslandMode, reduceMotion: boolean): Transition {
  const toCollapsed = mode === "collapsed";
  const toExpanded = mode === "expanded";
  const sizeDuration = reduceMotion
    ? 0
    : toCollapsed
      ? METRICS.collapseDuration
      : toExpanded
        ? METRICS.expandDuration
        : METRICS.peekDuration;
  const sizeEase = balanced;

  const collapsedDelay = reduceMotion ? 0 : toCollapsed ? 50 : 0;
  const collapsedDuration = reduceMotion ? 0 : toCollapsed ? 120 : 90;
  const contentDelay = reduceMotion ? 0 : toCollapsed ? 0 : toExpanded ? 40 : 80;
  const contentDuration = reduceMotion ? 0 : toCollapsed ? 100 : 170;
  const controlsDelay = reduceMotion ? 0 : toExpanded ? 140 : 0;
  const controlsDuration = reduceMotion ? 0 : toExpanded ? 140 : 90;
  const bottomDelay = reduceMotion ? 0 : toExpanded ? 170 : 0;
  const bottomDuration = reduceMotion ? 0 : toCollapsed ? 100 : 120;

  const transition: Transition = {
    width: new Track(from.width, to.width, 0, sizeDuration, sizeEase),
    height: new Track(from.height, to.height, 0, sizeDuration, sizeEase),
    cornerRadius: new Track(from.cornerRadius, to.cornerRadius, 0, sizeDuration, sizeEase),
    collapsedOpacity: new Track(from.collapsedOpacity, to.collapsedOpacity, collapsedDelay, collapsedDuration, outCubic),
    contentOpacity: new Track(from.contentOpacity, to.contentOpacity, contentDelay, contentDuration, outCubic),
    contentOffsetY: new Track(from.contentOffsetY, to.contentOffsetY, toCollapsed ? 0 : contentDelay, toCollapsed ? 100 : contentDuration, outCubic),
    controlsOpacity: new Track(from.controlsOpacity, to.controlsOpacity, controlsDelay, controlsDuration, outCubic),
    bottomOpacity: new Track(from.bottomOpacity, to.bottomOpacity, bottomDelay, bottomDuration, outCubic),
    bottomOffsetY: new Track(from.bottomOffsetY, to.bottomOffsetY, bottomDelay, bottomDuration, outCubic),
    total: 0,
  };
  transition.total = Math.max(
    transition.width.delay + transition.width.duration,
    transition.height.delay + transition.height.duration,
    transition.cornerRadius.delay + transition.cornerRadius.duration,
    transition.collapsedOpacity.delay + transition.collapsedOpacity.duration,
    transition.contentOpacity.delay + transition.contentOpacity.duration,
    transition.contentOffsetY.delay + transition.contentOffsetY.duration,
    transition.controlsOpacity.delay + transition.controlsOpacity.duration,
    transition.bottomOpacity.delay + transition.bottomOpacity.duration,
    transition.bottomOffsetY.delay + transition.bottomOffsetY.duration,
  );
  return transition;
}
