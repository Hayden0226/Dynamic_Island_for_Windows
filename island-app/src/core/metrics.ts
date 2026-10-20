export const METRICS = {
  collapsedWidth: 176,
  collapsedCompactWidth: 104,
  collapsedHeight: 48,
  peekWidth: 380,
  peekHeight: 150,
  expandedWidth: 380,
  expandedHeight: 220,
  expandedCompactHeight: 112,
  cornerRadius: 24,
  cornerSmoothing: 0.6,
  windowWidth: 396,
  windowHeight: 254,
  surfaceTop: 26,
  topOffset: 14,
  opticalOffsetX: -1,
  expandDuration: 240,
  collapseDuration: 260,
  peekDuration: 300,
  collapseDelay: 140,
  contentFade: 180,
  contentRise: 10,
} as const;

export type IslandMode = "collapsed" | "peek" | "expanded";

export interface IslandFrame {
  width: number;
  height: number;
  cornerRadius: number;
  collapsedOpacity: number;
  contentOpacity: number;
  contentOffsetY: number;
}

