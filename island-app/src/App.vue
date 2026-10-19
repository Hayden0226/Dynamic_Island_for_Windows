<script setup lang="ts">
import { onMounted, onUnmounted } from "vue";
import IslandSurface from "./components/IslandSurface.vue";
import { initStore, disposeStore } from "./store/island";

onMounted(initStore);
onUnmounted(disposeStore);
</script>

<template>
  <IslandSurface />
</template>
