using Godot;
using System;
using System.Text.Json;
using System.Text;

public partial class GameClient : Node
{
    [Signal] public delegate void ConnectedEventHandler();
    [Signal] public delegate void DisconnectedEventHandler();
    [Signal] public delegate void MessageReceivedEventHandler(string type, string json);
    [Signal] public delegate void CharacterStatusReceivedEventHandler(Variant data);
    [Signal] public delegate void SpellbookUpdatedEventHandler(Variant data);
    [Signal] public delegate void SpellbookFullReceivedEventHandler(Variant data);
    [Signal] public delegate void CombatLogReceivedEventHandler(Variant data);
    [Signal] public delegate void BuffsUpdatedEventHandler(Variant data);
    [Signal] public delegate void InventoryUpdatedEventHandler(Variant data);
    [Signal] public delegate void ZoneStateReceivedEventHandler(Variant data);
    [Signal] public delegate void MobMoveReceivedEventHandler(Variant data);
    [Signal] public delegate void EnvironmentUpdatedEventHandler(Variant data);
    [Signal] public delegate void EntitySneakReceivedEventHandler(Variant data);
    [Signal] public delegate void EntityHideReceivedEventHandler(Variant data);
    [Signal] public delegate void SneakResultReceivedEventHandler(Variant data);
    [Signal] public delegate void HideResultReceivedEventHandler(Variant data);
    [Signal] public delegate void SneakBrokenReceivedEventHandler();
    [Signal] public delegate void HideBrokenReceivedEventHandler();
    [Signal] public delegate void LoginOkReceivedEventHandler(Variant data);
    [Signal] public delegate void NpcSayReceivedEventHandler(Variant data);
    [Signal] public delegate void MerchantOpenedEventHandler(Variant data);
    [Signal] public delegate void TrainerOpenedEventHandler(Variant data);
    [Signal] public delegate void BankOpenedEventHandler(Variant data);
    [Signal] public delegate void AccountOkReceivedEventHandler(Variant data);
    [Signal] public delegate void CharacterCreatedEventHandler(Variant data);
    [Signal] public delegate void DeityListReceivedEventHandler(Variant data);
    [Signal] public delegate void CharCreateDataReceivedEventHandler(Variant data);
    [Signal] public delegate void CharacterDeletedEventHandler(Variant data);
    [Signal] public delegate void ChatReceivedEventHandler(Variant data);
    [Signal] public delegate void CampCompleteEventHandler();

    public static GameClient Instance { get; private set; }
    
    public string LastStatusPayload { get; private set; } = "";
    public string LastInventoryPayload { get; private set; } = "";
    public string LastSpellbookPayload { get; private set; } = "";
    public string LastSpellbookFullPayload { get; private set; } = "";
    public string LastZoneStatePayload { get; private set; } = "";
    public string LastBuffsPayload { get; private set; }

    private WebSocketPeer _socket = new WebSocketPeer();
    private string _url = "ws://localhost:3005";
    public string ServerUrl { get => _url; set => _url = value; }
    private bool _connected = false;
    public bool IsSocketConnected => _connected;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        // Don't auto-connect at boot
    }

    public void ConnectToServer()
    {
        GD.Print($"[NET] Connecting to {_url}...");
        // Increase buffer sizes for large ZONE_STATE payloads (386+ entities)
        _socket.InboundBufferSize = 4 * 1024 * 1024;  // 4MB incoming
        _socket.OutboundBufferSize = 1 * 1024 * 1024;  // 1MB outgoing
        Error err = _socket.ConnectToUrl(_url);
        if (err != Error.Ok)
        {
            GD.PrintErr($"[NET] Could not connect: {err}");
        }
    }

    public override void _Process(double delta)
    {
        _socket.Poll();

        var state = _socket.GetReadyState();
        if (state == WebSocketPeer.State.Open)
        {
            if (!_connected)
            {
                _connected = true;
                GD.Print("[NET] WebSocket Connected!");
                EmitSignal(SignalName.Connected);
            }

            while (_socket.GetAvailablePacketCount() > 0)
            {
                byte[] packet = _socket.GetPacket();
                string message = Encoding.UTF8.GetString(packet);
                HandleMessage(message);
            }
        }
        else if (state == WebSocketPeer.State.Closed)
        {
            if (_connected)
            {
                _connected = false;
                GD.Print("[NET] WebSocket Closed.");
                EmitSignal(SignalName.Disconnected);
                GetTree().CreateTimer(5.0).Timeout += ConnectToServer;
            }
        }
    }

    private void HandleMessage(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            string type = root.GetProperty("type").GetString();

            EmitSignal(SignalName.MessageReceived, type, message);

            switch (type)
            {
                case "ACCOUNT_OK":
                    EmitSignal(SignalName.AccountOkReceived, message);
                    break;
                case "CHARACTER_CREATED":
                    EmitSignal(SignalName.CharacterCreated, message);
                    break;
                case "DEITY_LIST":
                    EmitSignal(SignalName.DeityListReceived, message);
                    break;
                case "CHARACTER_DELETED":
                    EmitSignal(SignalName.CharacterDeleted, message);
                    break;
                case "CHAR_CREATE_DATA":
                    EmitSignal(SignalName.CharCreateDataReceived, message);
                    break;
                case "LOGIN_OK":
                    LastStatusPayload = message;
                    EmitSignal(SignalName.LoginOkReceived, message);
                    EmitSignal(SignalName.CharacterStatusReceived, message);
                    EmitSignal(SignalName.InventoryUpdated, message);
                    break;
                case "STATUS":
                    LastStatusPayload = message;
                    EmitSignal(SignalName.CharacterStatusReceived, message);
                    break;
                case "SPELLBOOK_UPDATE":
                    LastSpellbookPayload = message;
                    EmitSignal(SignalName.SpellbookUpdated, message);
                    break;
                case "SPELLBOOK_FULL":
                    LastSpellbookFullPayload = message;
                    EmitSignal(SignalName.SpellbookFullReceived, message);
                    break;
                case "ZONE_STATE":
                    LastZoneStatePayload = message;
                    EmitSignal(SignalName.ZoneStateReceived, message);
                    break;
                case "MOB_MOVE":
                    EmitSignal(SignalName.MobMoveReceived, message);
                    break;
                case "ENVIRONMENT_UPDATE":
                    EmitSignal(SignalName.EnvironmentUpdated, message);
                    break;
                case "ENTITY_SNEAK":
                    EmitSignal(SignalName.EntitySneakReceived, message);
                    break;
                case "ENTITY_HIDE":
                    EmitSignal(SignalName.EntityHideReceived, message);
                    break;
                case "SNEAK_RESULT":
                    EmitSignal(SignalName.SneakResultReceived, message);
                    break;
                case "HIDE_RESULT":
                    EmitSignal(SignalName.HideResultReceived, message);
                    break;
                case "SNEAK_BROKEN":
                    EmitSignal(SignalName.SneakBrokenReceived);
                    break;
                case "HIDE_BROKEN":
                    EmitSignal(SignalName.HideBrokenReceived);
                    break;
                case "COMBAT_LOG":
                    EmitSignal(SignalName.CombatLogReceived, message);
                    break;
                case "INVENTORY_UPDATE":
                    LastInventoryPayload = message;
                    EmitSignal(SignalName.InventoryUpdated, message);
                    break;
                case "BUFFS_UPDATE":
                    LastBuffsPayload = message;
                    EmitSignal(SignalName.BuffsUpdated, message);
                    break;
                case "NPC_SAY":
                    EmitSignal(SignalName.NpcSayReceived, message);
                    break;
                case "OPEN_MERCHANT":
                    EmitSignal(SignalName.MerchantOpened, message);
                    break;
                case "OPEN_TRAINER":
                    EmitSignal(SignalName.TrainerOpened, message);
                    break;
                case "OPEN_BANK":
                    EmitSignal(SignalName.BankOpened, message);
                    break;
                case "CHAT":
                    EmitSignal(SignalName.ChatReceived, message);
                    break;
                case "CAMP_COMPLETE":
                    EmitSignal(SignalName.CampComplete);
                    break;
                case "TELEPORT":
                {
                    float tx = root.TryGetProperty("x", out var txp) ? txp.GetSingle() : 0f;
                    float ty = root.TryGetProperty("y", out var typ) ? typ.GetSingle() : 0f;
                    float tz = root.TryGetProperty("z", out var tzp) ? tzp.GetSingle() : 0f;
                    GD.Print($"[NET] Teleport to EQ ({tx}, {ty}, {tz})");
                    var wm = GetTree().Root.GetNodeOrNull<WorldManager>("MainUI/ViewPortPanel/SubViewportContainer/SubViewport/World3D");
                    if (wm != null)
                        wm.TeleportPlayer(tx, ty, tz);
                    break;
                }
                case "WELCOME":
                    GD.Print($"[NET] {type}: Server ready.");
                    break;
                case "ERROR":
                    GD.PrintErr($"[NET] Server error: {root.GetProperty("message").GetString()}");
                    break;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NET] Error parsing message: {ex.Message}");
        }
    }

    public void SendMessage(string type, object data = null)
    {
        if (_socket.GetReadyState() != WebSocketPeer.State.Open) return;

        string json;
        if (data == null) {
            json = JsonSerializer.Serialize(new { type = type });
        } else {
            // Use a JsonDocument to merge the 'type' field with the existing data object
            string dataJson = JsonSerializer.Serialize(data);
            using var doc = JsonDocument.Parse(dataJson);
            
            var dict = new System.Collections.Generic.Dictionary<string, object>();
            dict["type"] = type;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                // Convert JsonElement values back to appropriate objects for the dictionary
                dict[prop.Name] = prop.Value.ValueKind switch {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => prop.Value.GetRawText()
                };
            }
            json = JsonSerializer.Serialize(dict);
        }
        
        _socket.SendText(json);
    }
    
    public void SendRaw(string json)
    {
        if (_socket.GetReadyState() == WebSocketPeer.State.Open)
            _socket.SendText(json);
    }
}
