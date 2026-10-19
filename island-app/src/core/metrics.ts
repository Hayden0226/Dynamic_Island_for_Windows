export const METRICS = {
  collapsedWidth: 176,
  collapsedHeight: 48,
  peekWidth: 380,
  peekHeight: 150,
  expandedWidth: 380,
  expandedHeight: 220,
  cornerRadius: 30,
  cornerSmoothing: 0.6,
  shadowMargin: 26,
  topOffset: 14,
  opticalOffsetX: -1,
  expandDuration: 360,
  collapseDuration: 340,
  peekDuration: 340,
  collapseDelay: 250,
  contentFade: 180,
} as const;

export type IslandMode = "collapsed" | "peek" | "expanded";

export interface IslandFrame {
  width: number;
  height: number;
  cornerRadius: number;
  collapsedOpacity: number;
  contentOpacity: number;
  contentOffsetY: number;
  controlsOpacity: number;
  bottomOpacity: number;
  bottomOffsetY: number;
}

