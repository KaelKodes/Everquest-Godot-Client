using Godot;

/// <summary>Persists floating bag panel positions in <see cref="UILayoutManager"/> under section <c>bag_positions</c>
/// (same files as other UI layouts: character + server + resolution).</summary>
public static class BagLayoutStore
{
    private static string SlotKey(int parentSlotId) => $"slot_{parentSlotId}";

    public static void Save(int parentSlotId, Vector2 globalPos)
    {
        if (string.IsNullOrEmpty(GameState.CharacterName)) return;

        var section = UILayoutManager.GetSection("bag_positions");
        var entry = new Godot.Collections.Dictionary();
        entry["x"] = globalPos.X;
        entry["y"] = globalPos.Y;
        section[SlotKey(parentSlotId)] = entry;
        UILayoutManager.SetSection("bag_positions", section);
        UILayoutManager.SaveLayout(GameState.CharacterName);
    }

    public static bool TryLoad(int parentSlotId, out Vector2 globalPos)
    {
        globalPos = default;
        if (string.IsNullOrEmpty(GameState.CharacterName)) return false;

        var section = UILayoutManager.GetSection("bag_positions");
        var key = SlotKey(parentSlotId);
        if (!section.ContainsKey(key)) return false;

        Variant v = section[key];
        if (v.VariantType != Variant.Type.Dictionary) return false;

        var inner = v.AsGodotDictionary();
        if (!inner.ContainsKey("x") || !inner.ContainsKey("y")) return false;

        globalPos = new Vector2(inner["x"].AsSingle(), inner["y"].AsSingle());
        return true;
    }

    public static Vector2 ClampGlobalPosition(Vector2 pos, Vector2 size, Rect2 visibleRect)
    {
        float w = size.X;
        float h = size.Y;
        float minX = visibleRect.Position.X + 4f;
        float minY = visibleRect.Position.Y + 4f;
        float maxX = visibleRect.Position.X + visibleRect.Size.X - w - 4f;
        float maxY = visibleRect.Position.Y + visibleRect.Size.Y - h - 4f;
        if (maxX < minX) maxX = minX;
        if (maxY < minY) maxY = minY;
        return new Vector2(
            Mathf.Clamp(pos.X, minX, maxX),
            Mathf.Clamp(pos.Y, minY, maxY));
    }
}
