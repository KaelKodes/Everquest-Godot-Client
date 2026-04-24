using Godot;
using System.Text.Json;

using System.Collections.Generic;

public partial class MudMap : Control
{
    public override void _Ready()
    {
        ClipContents = true;
    }

    public class ZoneLine 
    {
        public string Target { get; set; }
        public string TargetLongName { get; set; }
        public string Edge { get; set; }
        public float Width { get; set; }
        public float Offset { get; set; }
        public bool HasCoords { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public string Orientation { get; set; } // "ns" or "ew"
        public float Length { get; set; }
    }

    private bool _hasData = false;
    private Vector2 _playerPos;
    private Vector2 _mapSize;
    private Vector2 _centerOffset;
    private List<ZoneLine> _zoneLines = new List<ZoneLine>();

    public void UpdateMap(JsonElement mapData, string currentRoomId, Vector2 playerPos, Vector2 mapSize, Vector2 centerOffset, JsonElement zoneLines)
    {
        _playerPos = playerPos;
        _mapSize = mapSize;
        _centerOffset = centerOffset;
        _zoneLines.Clear();

        if (zoneLines.ValueKind == JsonValueKind.Array)
        {
            foreach (var zl in zoneLines.EnumerateArray())
            {
                float zpX = zl.TryGetProperty("x", out var xp) ? xp.GetSingle() : 0;
                bool hasCoords = zpX != 0;

                string shortName = zl.TryGetProperty("target", out var t) ? t.GetString() : "Unknown";
                var zlObj = new ZoneLine
                {
                    Target = shortName,
                    TargetLongName = zl.TryGetProperty("targetLongName", out var tln) ? tln.GetString() : shortName,
                    Edge = zl.TryGetProperty("edge", out var e) ? e.GetString() : "north",
                    Width = zl.TryGetProperty("width", out var w) ? w.GetSingle() : 400f,
                    Offset = zl.TryGetProperty("offset", out var o) ? o.GetSingle() : 0f,
                    HasCoords = hasCoords,
                    X = zpX,
                    Y = zl.TryGetProperty("y", out var yp) ? yp.GetSingle() : 0,
                    Orientation = zl.TryGetProperty("orientation", out var op) ? op.GetString() : "ns",
                    Length = zl.TryGetProperty("length", out var lp) ? lp.GetSingle() : 100f
                };
                _zoneLines.Add(zlObj);
            }
        }

        _hasData = true;


        
        QueueRedraw();
        TooltipText = "";
    }

    public override void _GuiInput(InputEvent @event)
    {
        // Removed legacy room-hover tooltip logic
    }

    public override void _Draw()
    {
        if (!_hasData || _mapSize.X <= 0 || _mapSize.Y <= 0) return;

        // Force size to map to parent if parent is set
        if (GetParent() is Control p) {
            Size = p.Size;
        }

        // Determine drawing area. Leave a padding so thick lines don't get clipped.
        float padding = 20f;
        Rect2 drawArea = new Rect2(padding, padding, Size.X - padding * 2, Size.Y - padding * 2);

        // Aspect Ratio calculation
        float mapAspect = _mapSize.X / _mapSize.Y;
        float areaAspect = drawArea.Size.X / drawArea.Size.Y;

        float boxW, boxH;
        if (mapAspect > areaAspect)
        {
            // Map is wider than the area; bind to width
            boxW = drawArea.Size.X;
            boxH = drawArea.Size.X / mapAspect;
        }
        else
        {
            // Map is taller than the area; bind to height
            boxH = drawArea.Size.Y;
            boxW = drawArea.Size.Y * mapAspect;
        }

        // Center the scaled bounding box inside the draw area
        Vector2 boxPos = new Vector2(
            drawArea.Position.X + (drawArea.Size.X - boxW) / 2f,
            drawArea.Position.Y + (drawArea.Size.Y - boxH) / 2f
        );
        Rect2 boxRect = new Rect2(boxPos, new Vector2(boxW, boxH));

        // Draw the zone base
        DrawRect(boxRect, new Color(0.15f, 0.25f, 0.45f, 0.8f), true); // Ocean/dark blue fill
        DrawRect(boxRect, Colors.SkyBlue, false, 2f); // Border

        // Scale factors to map real coordinates to UI pixels
        float scaleX = boxW / _mapSize.X;
        float scaleY = boxH / _mapSize.Y;

        // Zone boundary extents for coordinate mapping
        float minX = _centerOffset.X - _mapSize.X / 2f;
        float minY = _centerOffset.Y - _mapSize.Y / 2f;

        // Draw Zone Lines
        foreach (var zl in _zoneLines)
        {
            if (zl.HasCoords)
            {
                // Coordinate-based zone point — draw oriented line at actual position
                float zlNormX = (zl.X - minX) / _mapSize.X;
                float zlNormY = (zl.Y - minY) / _mapSize.Y;
                
                // EQ: +Y = North (top), +X = West (left)
                float drawX = boxRect.Position.X + (1f - zlNormX) * boxW;
                float drawY = boxRect.Position.Y + (1f - zlNormY) * boxH;
                
                // Draw a line matching the passage orientation
                // 'ew' = passage runs east-west → line is horizontal on map
                // 'ns' = passage runs north-south → line is vertical on map
                float lineLen = Mathf.Max(zl.Length * Mathf.Max(scaleX, scaleY), 8f);
                Vector2 start, end;
                if (zl.Orientation == "ew")
                {
                    // Horizontal line (east-west passage)
                    start = new Vector2(drawX - lineLen / 2f, drawY);
                    end = new Vector2(drawX + lineLen / 2f, drawY);
                }
                else
                {
                    // Vertical line (north-south passage)
                    start = new Vector2(drawX, drawY - lineLen / 2f);
                    end = new Vector2(drawX, drawY + lineLen / 2f);
                }
                
                DrawLine(start, end, Colors.LightGreen, 6f);
                
                // Label
                var font = ThemeDB.FallbackFont;
                if (font != null)
                {
                    DrawString(font, new Vector2(end.X + 4, end.Y + 4), zl.TargetLongName, HorizontalAlignment.Left, -1, 9, Colors.LightGreen);
                }
            }
            else
            {
                // Legacy edge-based zone line
                float scaledOffset = zl.Offset * (zl.Edge == "north" || zl.Edge == "south" ? scaleX : scaleY);
                float scaledWidth = zl.Width * (zl.Edge == "north" || zl.Edge == "south" ? scaleX : scaleY);
                
                Vector2 start = Vector2.Zero;
                Vector2 end = Vector2.Zero;
                
                float midX2 = boxRect.Position.X + boxRect.Size.X / 2f;
                float midY2 = boxRect.Position.Y + boxRect.Size.Y / 2f;

                if (zl.Edge == "north")
                {
                    start = new Vector2(midX2 - scaledOffset - scaledWidth / 2f, boxRect.Position.Y);
                    end = new Vector2(midX2 - scaledOffset + scaledWidth / 2f, boxRect.Position.Y);
                }
                else if (zl.Edge == "south")
                {
                    start = new Vector2(midX2 - scaledOffset - scaledWidth / 2f, boxRect.Position.Y + boxRect.Size.Y);
                    end = new Vector2(midX2 - scaledOffset + scaledWidth / 2f, boxRect.Position.Y + boxRect.Size.Y);
                }
                else if (zl.Edge == "west")
                {
                    start = new Vector2(boxRect.Position.X, midY2 - scaledOffset - scaledWidth / 2f);
                    end = new Vector2(boxRect.Position.X, midY2 - scaledOffset + scaledWidth / 2f);
                }
                else if (zl.Edge == "east")
                {
                    start = new Vector2(boxRect.Position.X + boxRect.Size.X, midY2 - scaledOffset - scaledWidth / 2f);
                    end = new Vector2(boxRect.Position.X + boxRect.Size.X, midY2 - scaledOffset + scaledWidth / 2f);
                }

                if (start != end)
                {
                    DrawLine(start, end, Colors.LightGreen, 6f);
                }
            }
        }

        // Calculate player position using the same minX/minY from above
        float normX = (_playerPos.X - minX) / _mapSize.X;
        float normY = (_playerPos.Y - minY) / _mapSize.Y;

        // Map norm coordinates (0 to 1) into the boxRect
        // By standard EQ convention: +Y is North, +X is West.
        // We want visually: Top is North, Left is West.
        // If Y goes up to go North (+1.0 normY), we want Top edge (0): (1.0 - normY)
        // If X goes up to go West (+1.0 normX), we want Left edge (0): (1.0 - normX)
        Vector2 dotPos = new Vector2(
            boxRect.Position.X + (1.0f - normX) * boxRect.Size.X,
            boxRect.Position.Y + (1.0f - normY) * boxRect.Size.Y
        );

        // Clamp dot strictly inside box to prevent overspill drawing glitches
        dotPos.X = Mathf.Clamp(dotPos.X, boxRect.Position.X, boxRect.Position.X + boxRect.Size.X);
        dotPos.Y = Mathf.Clamp(dotPos.Y, boxRect.Position.Y, boxRect.Position.Y + boxRect.Size.Y);

        // Draw Player Marker
        DrawCircle(dotPos, 4f, Colors.White);
        DrawCircle(dotPos, 5f, Colors.Red, false, 1f);
        DrawString(ThemeDB.FallbackFont, dotPos + new Vector2(6, 4), "You", HorizontalAlignment.Left, -1, 10, Colors.White);
    }
}
