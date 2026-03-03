package npc

import "github.com/kenyamaneko/overload-party-common/model"

// NPCDeckDefinition defines a pre-built NPC deck.
type NPCDeckDefinition struct {
	Name    string
	Faction string
	Cards   []int64 // card_no list, exactly 30 cards
}

// NPCDecks maps faction name to a fixed NPC deck.
// Card numbers must correspond to existing CardDefinitions rows.
// Cards follow restriction rules: unlimited=3 copies, semi_limited=2, limited=1.
var NPCDecks = map[string]*NPCDeckDefinition{
	model.FactionSD:      sdDeck,
	model.FactionTenki:   tenkiDeck,
	model.FactionSugar: sugarDeck,
	model.FactionTuners:  tunersDeck,
}

// GetNPCDeck returns an NPC deck for the given faction, or nil if not found.
func GetNPCDeck(faction string) *NPCDeckDefinition {
	return NPCDecks[faction]
}

// DevNPCDeck is a test deck built from dev_cards.sql (8 cards).
// Uses card 1 (SD Compute, unlimited) ×3, card 2 (Tenki Serverless, unlimited) ×3,
// card 3 (Tenki Database, unlimited) ×3, card 4 (Sugar Container, semi_limited) ×2,
// card 5 (Tuners Database, limited) ×1, card 6 (Neutral Platform, unlimited) ×3,
// card 7 (Neutral Incident, semi_limited) ×2, card 8 (SD Attachment, unlimited) ×3,
// Repeat card 1-3 for remaining slots to reach 30.
var DevNPCDeck = &NPCDeckDefinition{
	Name:    "Dev Test Deck",
	Faction: "Mixed",
	Cards: []int64{
		1, 1, 1, // SD Compute ×3
		2, 2, 2, // Tenki Serverless ×3
		3, 3, 3, // Tenki Database ×3
		4, 4, // Sugar Container ×2
		5,       // Tuners Database ×1
		6, 6, 6, // Neutral Platform ×3
		7, 7, // Neutral Incident ×2
		8, 8, 8, // SD Attachment ×3
		1, 1, 1, // padding: SD Compute ×3 (extra copies for dev)
		2, 2, 2, // padding: Tenki Serverless ×3
		3, 3, 3, // padding: Tenki Database ×3
	},
}

// Faction decks — generated from DECK_SAMPLES.md
// See scripts/deck_samples_to_cardnos.py for generation
var sdDeck = &NPCDeckDefinition{
	Name:    "SD Standard",
	Faction: model.FactionSD,
	Cards: []int64{
		1, 1, 1, 3, 6, 6, 7, 7, 7, 8,
		8, 8, 9, 9, 13, 13, 15, 17, 20, 20,
		20, 98, 99, 100, 101, 101, 115, 118, 119, 121,
	},
}

var tenkiDeck = &NPCDeckDefinition{
	Name:    "Tenki Standard",
	Faction: model.FactionTenki,
	Cards: []int64{
		23, 23, 26, 26, 26, 27, 27, 29, 29, 29,
		31, 32, 32, 32, 35, 35, 37, 38, 38, 38,
		41, 46, 46, 94, 98, 99, 100, 101, 101, 117,
	},
}

var sugarDeck = &NPCDeckDefinition{
	Name:    "Sugar Standard",
	Faction: model.FactionSugar,
	Cards: []int64{
		47, 47, 47, 48, 48, 48, 49, 49, 50, 50,
		51, 51, 52, 55, 56, 58, 58, 60, 60, 61,
		62, 62, 66, 98, 99, 100, 104, 104, 104, 106,
	},
}

var tunersDeck = &NPCDeckDefinition{
	Name:    "Tuners Standard",
	Faction: model.FactionTuners,
	Cards: []int64{
		70, 70, 70, 72, 74, 74, 74, 76, 76, 77,
		77, 77, 78, 79, 79, 81, 82, 84, 85, 86,
		86, 86, 89, 89, 90, 90, 100, 101, 101, 101,
	},
}
