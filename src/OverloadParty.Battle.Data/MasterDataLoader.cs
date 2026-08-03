using System.Text.Json;
using ApiCard = OverloadParty.ApiCard;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data;

/// <summary>card が配布するマスターデータの JSON を battle のドメインモデルへ変換する。</summary>
public static class MasterDataLoader
{
    private const string CardsLabel = "cards";
    private const string InitiativesLabel = "initiatives";

    /// <summary>カード定義と施策定義の JSON を読み込む。</summary>
    /// <param name="cardsJson">カード定義の JSON。</param>
    /// <param name="initiativesJson">施策定義の JSON。</param>
    /// <returns>読み込んだカード定義と施策定義。</returns>
    public static (List<CardDefinition> Cards, List<Initiative> Initiatives) FromJson(
        string cardsJson, string initiativesJson) =>
        (ReadEntries<ApiCard.CardDefinition>(cardsJson, CardsLabel)
            .Select(CardDefinitionMapper.ToCardDefinition).ToList(),
         ReadEntries<ApiCard.Initiative>(initiativesJson, InitiativesLabel)
            .Select(CardDefinitionMapper.ToInitiative).ToList());

    private static List<T> ReadEntries<T>(string json, string label)
    {
        var entries = JsonSerializer.Deserialize<List<T>>(json)
            ?? throw new InvalidOperationException($"failed to deserialize {label} master data");
        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"{label} master data has no entries");
        }

        return entries;
    }
}
