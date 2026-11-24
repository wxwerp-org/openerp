namespace OpenERP.Api.Erp;

/// <summary>本地店铺目录中的一家店铺；同步时由上游全量数据替换。</summary>
internal sealed class Shop : Entity
{
    private string _name = "";

    public int Sid { get; set; }

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            SearchName = value.ToUpperInvariant();
        }
    }

    public string? SearchName { get; set; } = "";

    public string Platform { get; set; } = "";

    public string? Region { get; set; }

    public string? MemberId { get; set; }

    public bool? IsTokenOk { get; set; }

    public string? TokenError { get; set; }

    public DateTimeOffset? SyncedAt { get; set; }
}

/// <summary>上游平台目录的本地副本，只保存展示元数据。</summary>
internal sealed class ShopPlatform : Entity
{
    public string Name { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string? Icon { get; set; }

    public bool IsProduction { get; set; }
}

/// <summary>店铺同步单行状态；只记录最近一次同步元数据，不保存授权历史。</summary>
internal sealed class ShopSyncState : Entity
{
    /// <summary>状态修订号；同步开始、完成或失败时递增，客户端据此丢弃旧快照。</summary>
    public long Version { get; set; }

    public DateTimeOffset? LastStartedAt { get; set; }

    public DateTimeOffset? LastCompletedAt { get; set; }

    public string? LastError { get; set; }

    public int Total { get; set; }

    public int Valid { get; set; }

    public int Invalid { get; set; }

    public int Unknown { get; set; }
}
