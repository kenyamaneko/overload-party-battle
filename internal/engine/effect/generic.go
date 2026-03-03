package effect

import (
	"fmt"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-common/model"
)

// --- Field Scanning Helpers ---

// CountFactionCards counts cards of the given faction on the field (frontend + backend + support).
func CountFactionCards(field *model.Field, faction string, cc *cache.CardCache) int {
	count := 0
	for _, res := range field.Frontend {
		if res != nil && res.FaceUp {
			if card := cc.Get(res.CardID); card != nil && card.Faction == faction {
				count++
			}
		}
	}
	for _, res := range field.Backend {
		if res != nil && res.FaceUp {
			if card := cc.Get(res.CardID); card != nil && card.Faction == faction {
				count++
			}
		}
	}
	for _, sup := range field.Support {
		if sup != nil && sup.DeployingTurnsLeft <= 0 {
			if card := cc.Get(sup.CardID); card != nil && card.Faction == faction {
				count++
			}
		}
	}
	return count
}

// CountBackendDBs counts backend DB cards (optionally of a specific faction).
// Only counts DB-type resources (Database, CacheDB), not ObjectStorage.
// If faction is "", counts all.
func CountBackendDBs(field *model.Field, faction string, cc *cache.CardCache) int {
	count := 0
	for _, res := range field.Backend {
		if res == nil || !res.FaceUp {
			continue
		}
		card := cc.Get(res.CardID)
		if card == nil {
			continue
		}
		// Only count DB types, not ObjectStorage
		if !model.IsDBType(card.CardType) {
			continue
		}
		if faction != "" && card.Faction != faction {
			continue
		}
		count++
	}
	return count
}

// HasCardOnField checks if a specific card_no exists on the field (any zone).
func HasCardOnField(field *model.Field, cardNo int64) bool {
	for _, res := range field.Frontend {
		if res != nil && res.FaceUp && res.CardID == cardNo {
			return true
		}
	}
	for _, res := range field.Backend {
		if res != nil && res.FaceUp && res.CardID == cardNo {
			return true
		}
	}
	return false
}

// HasStorageOnField checks if any Storage card exists on the field.
func HasStorageOnField(field *model.Field, cc *cache.CardCache) bool {
	for _, res := range field.Backend {
		if res == nil || !res.FaceUp {
			continue
		}
		card := cc.Get(res.CardID)
		if card != nil && card.CardType == "ObjectStorage" {
			return true
		}
	}
	return false
}

// HasCardTypeOnField checks if a card of a specific type and faction exists.
func HasCardTypeOnField(field *model.Field, cardType, faction string, cc *cache.CardCache) bool {
	for _, res := range field.Frontend {
		if res == nil || !res.FaceUp {
			continue
		}
		card := cc.Get(res.CardID)
		if card != nil && card.CardType == cardType && (faction == "" || card.Faction == faction) {
			return true
		}
	}
	for _, res := range field.Backend {
		if res == nil || !res.FaceUp {
			continue
		}
		card := cc.Get(res.CardID)
		if card != nil && card.CardType == cardType && (faction == "" || card.Faction == faction) {
			return true
		}
	}
	return false
}

// Security platform card numbers.
var securityPlatformCardNos = map[int64]bool{
	15: true, // Smile Firewall
	37: true, // 天気使い Protection
	84: true, // 調律部 WAF
}

// HasSecurityPlatform checks if the player has any Security-type platform on field.
func HasSecurityPlatform(field *model.Field, cc *cache.CardCache) bool {
	for _, sup := range field.Support {
		if sup == nil {
			continue
		}
		if securityPlatformCardNos[sup.CardID] {
			return true
		}
	}
	return false
}

// CountOpponentBackend counts the opponent's backend resources.
func CountOpponentBackend(state *model.GameState, playerNum int64) int {
	oppNum := model.OpponentNum(playerNum)
	field, err := state.GetField(oppNum)
	if err != nil {
		return 0
	}
	count := 0
	for _, res := range field.Backend {
		if res != nil {
			count++
		}
	}
	return count
}

// --- Common Effect Operations ---

// addToHand adds a card to the player's hand.
func addToHand(state *model.GameState, playerNum int64, cardNo int64) error {
	hand, err := state.GetHand(playerNum)
	if err != nil {
		return fmt.Errorf("get hand: %w", err)
	}
	hand = append(hand, model.HandCard{
		InstanceID: state.NextInstanceID(),
		CardID:     cardNo,
	})
	return state.SetHand(playerNum, hand)
}

// removeFromRepo removes a specific card_no from the repository. Returns false if not found.
func removeFromRepo(state *model.GameState, playerNum int64, cardNo int64) (bool, error) {
	repo, err := state.GetRepository(playerNum)
	if err != nil {
		return false, fmt.Errorf("get repository: %w", err)
	}
	for i, c := range repo {
		if c == cardNo {
			repo = append(repo[:i], repo[i+1:]...)
			if err := state.SetRepository(playerNum, repo); err != nil {
				return false, fmt.Errorf("set repository: %w", err)
			}
			return true, nil
		}
	}
	return false, nil
}

// addToTrash adds a card_no to the player's trash.
func addToTrash(state *model.GameState, playerNum int64, cardNo int64) error {
	trash, err := state.GetTrash(playerNum)
	if err != nil {
		return fmt.Errorf("get trash: %w", err)
	}
	trash = append(trash, cardNo)
	return state.SetTrash(playerNum, trash)
}

// removeFromTrash removes a specific card_no from trash. Returns false if not found.
func removeFromTrash(state *model.GameState, playerNum int64, cardNo int64) (bool, error) {
	trash, err := state.GetTrash(playerNum)
	if err != nil {
		return false, fmt.Errorf("get trash: %w", err)
	}
	for i, c := range trash {
		if c == cardNo {
			trash = append(trash[:i], trash[i+1:]...)
			if err := state.SetTrash(playerNum, trash); err != nil {
				return false, fmt.Errorf("set trash: %w", err)
			}
			return true, nil
		}
	}
	return false, nil
}

// --- Choice Types ---

// ChoiceCardNo is the common choice data for selecting a card.
type ChoiceCardNo struct {
	CardNo int64 `json:"cardNo"`
}

// ChoiceInstanceID is the common choice data for selecting an instance.
type ChoiceInstanceID struct {
	InstanceID string `json:"instanceId"`
}

// ChoiceOption is the common choice data for selecting an option.
type ChoiceOption struct {
	Option string `json:"option"`
}

// --- Incident Damage Reduction ---

// IncidentDamageReductionAmount is the per-source reduction applied by
// security-type attachments/platforms against incident damage.
const IncidentDamageReductionAmount = int64(200)

// Card numbers for incident damage reduction passives.
var incidentReductionAttachments = map[int64]int64{
	39: IncidentDamageReductionAmount, // 天気使い Entra
	16: IncidentDamageReductionAmount, // Security Group
	87: IncidentDamageReductionAmount, // 調律部 RAC
}

const (
	cardNoISMSCert  = int64(96) // ISMS Certification
	cardNoAutoPatch = int64(24) // Auto Patch
)

// applyIncidentDamageReduction reduces incident damage based on passive effects.
func applyIncidentDamageReduction(baseDamage int64, target *model.ResourceInstance, field *model.Field, cc *cache.CardCache) int64 {
	reduction := int64(0)

	// Check target's attachments for damage reduction
	for _, att := range target.Attachments {
		if red, ok := incidentReductionAttachments[att.CardID]; ok {
			reduction += red
		}
	}

	// Check support zone for ISMS Certification
	for _, sup := range field.Support {
		if sup != nil && sup.CardID == cardNoISMSCert {
			reduction += IncidentDamageReductionAmount
		}
	}

	// Check target's own passive (Auto Patch)
	if target.CardID == cardNoAutoPatch {
		reduction += IncidentDamageReductionAmount
	}

	result := baseDamage - reduction
	if result < 0 {
		result = 0
	}
	return result
}

// DestroyResources checks all resources on a field and moves destroyed ones to trash.
func DestroyResources(state *model.GameState, field *model.Field, playerNum int64, cc *cache.CardCache) error {
	for i, res := range field.Frontend {
		if res != nil && model.CalculateEffectiveAV(res) <= 0 {
			clearMigrationLink(field, res)
			ApplySLAPenalty(state, playerNum, res, cc)
			if err := addToTrash(state, playerNum, res.CardID); err != nil {
				return fmt.Errorf("add frontend resource to trash: %w", err)
			}
			field.Frontend[i] = nil
		}
	}
	for i, res := range field.Backend {
		if res != nil && model.CalculateEffectiveAV(res) <= 0 {
			clearMigrationLink(field, res)
			ApplySLAPenalty(state, playerNum, res, cc)
			if err := addToTrash(state, playerNum, res.CardID); err != nil {
				return fmt.Errorf("add backend resource to trash: %w", err)
			}
			field.Backend[i] = nil
		}
	}
	return nil
}

// clearMigrationLink clears the migration target's MigratingFrom if this resource
// was a migration source. This auto-unlocks the target when the source is destroyed.
func clearMigrationLink(field *model.Field, destroyed *model.ResourceInstance) {
	if destroyed.MigrationTarget == nil {
		return
	}
	targetID := *destroyed.MigrationTarget
	for _, res := range field.Frontend {
		if res != nil && res.InstanceID == targetID {
			res.MigratingFrom = nil
			res.MigratingOnTurn = 0
			return
		}
	}
	for _, res := range field.Backend {
		if res != nil && res.InstanceID == targetID {
			res.MigratingFrom = nil
			res.MigratingOnTurn = 0
			return
		}
	}
}

// ApplySLAPenalty deducts SLA penalty from the resource owner's budget.
func ApplySLAPenalty(state *model.GameState, playerNum int64, res *model.ResourceInstance, cc *cache.CardCache) {
	card := cc.Get(res.CardID)
	if card == nil {
		return
	}
	var penalty int64
	if model.IsComputeType(card.CardType) {
		stats, _ := model.ParseComputeStats(card.Stats)
		if stats != nil {
			penalty = stats.SLAPenalty
		}
	} else if model.IsDataType(card.CardType) {
		stats, _ := model.ParseDataStats(card.Stats)
		if stats != nil {
			penalty = stats.SLAPenalty
		}
	}
	if penalty > 0 {
		budget := state.GetBudget(playerNum)
		state.SetBudget(playerNum, budget-penalty)
	}
}

// --- Deploy Helper ---

// deployResourceFromRepo finds a card in the repository and deploys it to an empty slot.
func deployResourceFromRepo(state *model.GameState, playerNum int64, cardNo int64, overrideAV int64, cc *cache.CardCache) error {
	found, err := removeFromRepo(state, playerNum, cardNo)
	if err != nil {
		return err
	}
	if !found {
		return fmt.Errorf("card %d not in repository", cardNo)
	}

	card := cc.Get(cardNo)
	if card == nil {
		return fmt.Errorf("card %d not found", cardNo)
	}

	field, err := state.GetField(playerNum)
	if err != nil {
		return err
	}

	instance, err := model.CreateResourceInstance(card, state.NextInstanceID())
	if err != nil {
		return err
	}
	if overrideAV > 0 {
		instance.MaxAV = overrideAV
		instance.Damage = 0
	}

	if err := placeResourceOnField(field, instance, card.CardType); err != nil {
		return err
	}

	return state.SetField(playerNum, field)
}

// deployResourceFromHand finds a card in the hand and deploys it to an empty slot.
func deployResourceFromHand(state *model.GameState, playerNum int64, cardNo int64, cc *cache.CardCache) error {
	hand, err := state.GetHand(playerNum)
	if err != nil {
		return err
	}

	handIdx := -1
	for i, c := range hand {
		if c.CardID == cardNo {
			handIdx = i
			break
		}
	}
	if handIdx == -1 {
		return fmt.Errorf("card %d not in hand", cardNo)
	}

	hand = append(hand[:handIdx], hand[handIdx+1:]...)
	if err := state.SetHand(playerNum, hand); err != nil {
		return err
	}

	card := cc.Get(cardNo)
	if card == nil {
		return fmt.Errorf("card %d not found", cardNo)
	}

	field, err := state.GetField(playerNum)
	if err != nil {
		return err
	}

	instance, err := model.CreateResourceInstance(card, state.NextInstanceID())
	if err != nil {
		return err
	}

	if err := placeResourceOnField(field, instance, card.CardType); err != nil {
		return err
	}

	return state.SetField(playerNum, field)
}

// placeResourceOnField places a resource instance in the best available zone slot.
// Compute: prefer frontend (attack), fallback backend (monetize).
// ObjectStorage: prefer backend (DV gen), fallback frontend (wall).
// Other data: backend only.
func placeResourceOnField(field *model.Field, instance *model.ResourceInstance, cardType string) error {
	if model.IsComputeType(cardType) {
		// Prefer frontend, fallback backend
		for i, slot := range field.Frontend {
			if slot == nil {
				field.Frontend[i] = instance
				return nil
			}
		}
		for i, slot := range field.Backend {
			if slot == nil {
				field.Backend[i] = instance
				return nil
			}
		}
		return fmt.Errorf("no empty slot for compute resource")
	}

	if cardType == "ObjectStorage" {
		// Prefer backend, fallback frontend
		for i, slot := range field.Backend {
			if slot == nil {
				field.Backend[i] = instance
				return nil
			}
		}
		for i, slot := range field.Frontend {
			if slot == nil {
				field.Frontend[i] = instance
				return nil
			}
		}
		return fmt.Errorf("no empty slot for ObjectStorage")
	}

	if model.IsDataType(cardType) {
		for i, slot := range field.Backend {
			if slot == nil {
				field.Backend[i] = instance
				return nil
			}
		}
		return fmt.Errorf("no empty backend slot")
	}

	return fmt.Errorf("card type %s cannot be auto-deployed", cardType)
}
