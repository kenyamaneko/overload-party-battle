namespace OverloadParty.Battle.Models;

/// <summary>
/// Card type string constants.
/// </summary>
public static class CardTypes
{
    // ─── Compute category ───────────────────────────────────
    public const string Compute = "Compute";
    public const string Container = "Container";
    public const string Orchestrator = "Orchestrator";
    public const string Serverless = "Serverless";
    public const string AiMl = "AI/ML";

    // ─── Data category ──────────────────────────────────────
    public const string Database = "Database";
    public const string ObjectStorage = "ObjectStorage";
    public const string CacheDB = "CacheDB";


    // ─── Support category ───────────────────────────────────
    public const string Platform = "Platform";
    public const string Attachment = "Attachment";
    public const string Strategy = "Strategy";
    public const string Reactive = "Reactive";
    public const string Incident = "Incident";
}
