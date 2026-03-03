package npc

import "strings"

const (
	NPCPlayerIDPrefix = "npc-"
	NPCPlayerID       = "npc-00000000-0000-0000-0000-000000000001"
)

// IsNPCPlayer returns true if the given player ID is an NPC.
func IsNPCPlayer(playerID string) bool {
	return strings.HasPrefix(playerID, NPCPlayerIDPrefix)
}
