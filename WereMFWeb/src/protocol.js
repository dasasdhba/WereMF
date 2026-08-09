export const FULL_SNAPSHOT_APIS = Object.freeze(["game_update_night", "game_update_day"]);
export const NIGHT_PATCH_API = "game_update_night_patch";

export function isFullSnapshotApi(api) {
  return FULL_SNAPSHOT_APIS.includes(api);
}

export function isNightPatchApi(api) {
  return api === NIGHT_PATCH_API;
}

export function isGameRequestApi(api) {
  return api.startsWith("request_") && !api.endsWith("_parse_error");
}

export function isGameUpdateApi(api) {
  return api.startsWith("game_update_");
}

export function normalizeGameMessage(message) {
  if (!message || typeof message !== "object") return null;
  if (message.type === "game_message") return message.payload && typeof message.payload === "object" ? message.payload : null;
  return message;
}

export function isPublicMessage(message) {
  return message?.message_type === "public";
}
