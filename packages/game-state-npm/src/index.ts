// Re-exports for the OpenAPI-typescript generated schemas. Consumers should
// import specific type aliases from here instead of indexing into `components`
// directly. The underlying generated file (`openapi.gen.ts`) is the source of
// truth; this barrel module only renames the schemas for ergonomic import.

import type { components, paths } from "./openapi.gen";

export type { components, paths };

type Schemas = components["schemas"];

// ─── Battle ↔ Gateway RPC (snake_case wire) ────────────────────────────
export type HealthResponse = Schemas["HealthResponse"];
export type NpcModelsResponse = Schemas["NpcModelsResponse"];
export type NpcModelEntry = Schemas["NpcModelEntry"];
export type BattleDeckCard = Schemas["BattleDeckCard"];
export type GameCreatedResult = Schemas["GameCreatedResult"];
export type ActionEvent = Schemas["ActionEvent"];
export type ActionResult = Schemas["ActionResult"];
export type NpcBattleRequest = Schemas["NpcBattleRequest"];
export type PvpBattleRequest = Schemas["PvpBattleRequest"];
export type GameActionRequest = Schemas["GameActionRequest"];
export type DevCard = Schemas["DevCard"];

// ─── Game state view (camelCase wire) ──────────────────────────────────
export type ClientGameState = Schemas["ClientGameState"];
export type PlayerView = Schemas["PlayerView"];
export type OpponentView = Schemas["OpponentView"];
export type Field = Schemas["Field"];
export type OpponentField = Schemas["OpponentField"];
export type DeployedResource = Schemas["DeployedResource"];
export type DeployedSupport = Schemas["DeployedSupport"];
export type HiddenDeployedSupport = Schemas["HiddenDeployedSupport"];
export type UndeployedCard = Schemas["UndeployedCard"];
export type TemporaryEffect = Schemas["TemporaryEffect"];
export type TurnControlsMessage = Schemas["TurnControlsMessage"];
export type AvailableAction = Schemas["AvailableAction"];

// ─── Event payloads ────────────────────────────────────────────────────
export type PlayCardEventData = Schemas["PlayCardEventData"];
export type AttachCardEventData = Schemas["AttachCardEventData"];
export type AttackEventData = Schemas["AttackEventData"];
export type ScaleUpEventData = Schemas["ScaleUpEventData"];
export type MonetizeEventData = Schemas["MonetizeEventData"];
export type DiscardHandEventData = Schemas["DiscardHandEventData"];
export type UseEffectEventData = Schemas["UseEffectEventData"];
export type PhaseChangeEventData = Schemas["PhaseChangeEventData"];
export type PhaseEndEventData = Schemas["PhaseEndEventData"];
export type TurnEndEventData = Schemas["TurnEndEventData"];
export type BattleStartEventData = Schemas["BattleStartEventData"];
export type TurnStartEventData = Schemas["TurnStartEventData"];
export type ReactiveRevealedEventData = Schemas["ReactiveRevealedEventData"];
export type SelectSlotEventData = Schemas["SelectSlotEventData"];
export type GameOverEventData = Schemas["GameOverEventData"];
