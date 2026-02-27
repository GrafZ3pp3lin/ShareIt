import type { RuntimeEnv } from "./runtime-config";

export {};

declare global {
  interface Window {
    env?: RuntimeEnv;
  }
}
