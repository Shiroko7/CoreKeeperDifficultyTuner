using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>
/// OnGUI overlay window for tuning difficulty. Reuses the same GUI.Window + rounded-rect
/// visual language as CombatMeterOverlay (dark rounded card, custom-drawn controls instead of
/// default IMGUI chrome). Adds a custom draggable slider control (IMGUI has no themeable
/// built-in slider that matches this look).
///
/// Deliberately no named presets (Easy/Normal/Hard/...): Core Keeper has no built-in combat
/// difficulty concept to source those from - plain multiplier sliders, all starting at
/// 1.0x ("unmodified vanilla").
///
/// "Health" sliders mean effective durability, applied as damage math (see
/// DifficultyDamageSystem) - a 2x-health mob takes half damage rather than having its stats
/// rewritten. So HP bars keep showing vanilla numbers and just deplete at a different rate;
/// percentages remain correct. That's the deliberate cost of never writing to the save file.
///
/// Sliders only edit a local draft - nothing is sent to the server until Save is clicked,
/// which both applies the change (server re-checks admin privileges) and persists it to disk.
/// This keeps mid-combat tweaking deliberate: dragging a slider can't accidentally change live
/// monster stats mid-fight, only clicking Save does. Closing the window without saving and
/// reopening it discards the draft (Toggle() re-syncs from the last saved values).
///
/// Only usable by admins: adminPrivileges is a client-side display/UX gate here (so a non-admin
/// simply never sees the panel), but the real trust boundary is server-side in
/// DifficultyRequestReceiveSystem, which re-checks admin level on every incoming request.
/// </summary>
internal sealed class DifficultyOverlay : MonoBehaviour
{
    private const float WindowWidth = 340f;
    private const float WindowHeight = 480f;
    private const float Margin = 5f;
    private const float ShadowOffset = 3f;
    private const float PanelRadius = 10f;
    private const float ControlRadius = 5f;
    private const float HeaderHeight = 26f;
    private const float ContentPadding = 10f;
    private const float SliderRowHeight = 36f;
    private const float SaveButtonHeight = 28f;

    private static readonly Color ShadowColor = new(0f, 0f, 0f, 0.45f);
    private static readonly Color PanelColor = new(0.075f, 0.078f, 0.095f, 0.97f);
    private static readonly Color DividerColor = new(1f, 1f, 1f, 0.08f);
    private static readonly Color CloseButtonColor = new(0.55f, 0.22f, 0.22f, 0.55f);
    private static readonly Color SliderTrackColor = new(1f, 1f, 1f, 0.07f);
    private static readonly Color SliderFillColor = new(0.36f, 0.62f, 0.92f, 0.9f);
    private static readonly Color SaveButtonColor = new(0.30f, 0.62f, 0.36f, 0.9f);
    private static readonly Color SaveButtonDisabledColor = new(1f, 1f, 1f, 0.05f);
    private static readonly Color ResetButtonColor = new(1f, 1f, 1f, 0.07f);

    private static DifficultyOverlay _instance;

    private Rect _windowRect = new(80f, 80f, WindowWidth, WindowHeight);
    private Texture2D _whiteTexture;
    private DifficultyValues _draft = DifficultyValues.Normal;

    private GUIStyle _titleStyle;
    private GUIStyle _infoLabelStyle;
    private GUIStyle _buttonLabelStyle;
    private GUIStyle _sliderLabelStyle;
    private GUIStyle _sliderValueStyle;
    private GUIStyle _saveLabelStyle;
    private GUIStyle _saveLabelDisabledStyle;
    private bool _stylesReady;

    internal static void EnsureInstance()
    {
        if (_instance != null)
        {
            return;
        }

        var go = new GameObject("[DifficultyTuner] Overlay");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<DifficultyOverlay>();
        _instance.enabled = false;
    }

    internal static string Toggle()
    {
        PlayerController player = Manager.main != null ? Manager.main.player : null;
        if (player == null)
        {
            return "You must be in a game to use this command.";
        }

        if (player.adminPrivileges <= 0)
        {
            return "You need admin privileges to adjust difficulty.";
        }

        EnsureInstance();
        _instance.enabled = !_instance.enabled;
        if (_instance.enabled)
        {
            // Re-syncing only on open (not every frame) is what makes closing-without-saving
            // act as "discard": the draft always starts from the last actually-saved values.
            _instance._draft = DifficultyRuntime.Current;
        }

        return _instance.enabled ? "Difficulty tuner shown." : "Difficulty tuner hidden.";
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        _whiteTexture = Texture2D.whiteTexture;
    }

    private void OnGUI()
    {
        EnsureStyles();
        _windowRect = GUI.Window(GetInstanceID(), _windowRect, DrawWindow, "", GUIStyle.none);
    }

    private void EnsureStyles()
    {
        if (_stylesReady)
        {
            return;
        }

        _stylesReady = true;

        _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        _titleStyle.normal.textColor = new Color(0.93f, 0.93f, 0.96f);

        _infoLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleLeft };
        _infoLabelStyle.normal.textColor = new Color(0.6f, 0.6f, 0.66f);

        _buttonLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        _buttonLabelStyle.normal.textColor = new Color(0.85f, 0.85f, 0.9f);

        _sliderLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
        _sliderLabelStyle.normal.textColor = new Color(0.85f, 0.85f, 0.9f);

        _sliderValueStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
        _sliderValueStyle.normal.textColor = Color.white;
        _sliderValueStyle.padding.right = 8;

        _saveLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _saveLabelStyle.normal.textColor = Color.white;

        _saveLabelDisabledStyle = new GUIStyle(_saveLabelStyle);
        _saveLabelDisabledStyle.normal.textColor = new Color(1f, 1f, 1f, 0.35f);
    }

    private void DrawWindow(int windowId)
    {
        var panelRect = new Rect(Margin, Margin, WindowWidth - Margin * 2f, WindowHeight - Margin * 2f);
        DrawRoundedRect(new Rect(panelRect.x + ShadowOffset, panelRect.y + ShadowOffset, panelRect.width, panelRect.height), ShadowColor, PanelRadius);
        DrawRoundedRect(panelRect, PanelColor, PanelRadius);

        DrawHeaderRow(panelRect);
        DrawFilledRect(new Rect(panelRect.x + ContentPadding, panelRect.y + HeaderHeight, panelRect.width - ContentPadding * 2f, 1f), DividerColor);

        bool hasUnsavedChanges = !_draft.Equals(DifficultyRuntime.Current);

        var content = new Rect(
            panelRect.x + ContentPadding,
            panelRect.y + HeaderHeight + 8f,
            panelRect.width - ContentPadding * 2f,
            panelRect.height - HeaderHeight - ContentPadding - 8f - SaveButtonHeight - 8f);

        GUILayout.BeginArea(content);

        // Read-only: the game's own Casual/Normal/Hard world mode, chosen once at world
        // creation and baked into every enemy's stats at spawn - not something this mod (or
        // anything else) can change after the fact.
        // Damage sliders are absolute: 100% always means vanilla-Normal damage regardless of
        // this world's mode, because that mode's flat x2/x0.5 is divided back out.
        // Health sliders stay relative to this world: they scale durability on top of whatever
        // HP the world's mode already baked in, which this mod can't read back.
        GUILayout.Label($"World mode: {GetWorldModeLabel()} (fixed at creation)", _infoLabelStyle);
        GUILayout.Label("Damage: absolute. Health: relative to this world.", _infoLabelStyle);
        GUILayout.Label("Health changes durability - HP bars keep vanilla numbers.", _infoLabelStyle);
        GUILayout.Space(4f);

        DrawSliderRow("Mob Health", ref _draft.MobHealthMult, 0.1f, 3f);
        DrawSliderRow("Mob Damage", ref _draft.MobDamageMult, 0f, 3f);
        DrawSliderRow("Boss Health", ref _draft.BossHealthMult, 0.1f, 3f);
        DrawSliderRow("Boss Damage", ref _draft.BossDamageMult, 0f, 3f);
        DrawSliderRow("Player Health", ref _draft.PlayerHealthMult, 0.1f, 3f);
        DrawSliderRow("Player Damage", ref _draft.PlayerDamageMult, 0f, 3f);
        DrawSliderRow("Multiplayer Scaling", ref _draft.MultiplayerScalingFactor, 0f, 2f);

        // Readout shows the resulting weighting at both ends of the real progression rather
        // than the raw exponent, which is meaningless on its own: T2 is roughly Glurch (the
        // first boss) and T20 is Core Commander (the last), so this reads as "what my sliders
        // actually do to the first boss vs the last boss".
        float earlyFactor = Mathf.Pow(2f / 10f, _draft.TierCurve);
        float lateFactor = Mathf.Pow(20f / 10f, _draft.TierCurve);
        string curveReadout = Mathf.Abs(_draft.TierCurve) < 0.005f
            ? "Flat"
            : $"T2 {earlyFactor:0.00}x / T20 {lateFactor:0.00}x";
        DrawSliderRow("Tier Curve (mobs/bosses)", ref _draft.TierCurve, -0.6f, 0.6f, curveReadout);

        GUILayout.EndArea();

        float footerWidth = panelRect.width - ContentPadding * 2f;
        float resetWidth = footerWidth * 0.32f;
        var resetRect = new Rect(panelRect.x + ContentPadding, content.yMax + 8f, resetWidth, SaveButtonHeight);
        var saveRect = new Rect(resetRect.xMax + 6f, content.yMax + 8f, footerWidth - resetWidth - 6f, SaveButtonHeight);

        DrawResetButton(resetRect);
        DrawSaveButton(saveRect, hasUnsavedChanges);

        GUI.DragWindow(new Rect(panelRect.x, panelRect.y, panelRect.width, HeaderHeight));
    }

    /// <summary>
    /// Sets the draft back to 1.0x everywhere (still requires Save to actually apply/persist
    /// it). This is a convenience for getting back to vanilla behaviour in one click, NOT a
    /// prerequisite for uninstalling: since nothing is ever written to the world save, simply
    /// deleting the mod leaves everything vanilla regardless of what the sliders were set to.
    /// </summary>
    private void DrawResetButton(Rect rect)
    {
        DrawRoundedRect(rect, ResetButtonColor, ControlRadius);
        GUI.Label(rect, "Reset", _buttonLabelStyle);

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
        {
            _draft = DifficultyValues.Normal;
        }
    }

    private void DrawHeaderRow(Rect panelRect)
    {
        var titleRect = new Rect(panelRect.x + ContentPadding, panelRect.y, 200f, HeaderHeight);
        GUI.Label(titleRect, "Difficulty Tuner", _titleStyle);

        float buttonSize = HeaderHeight - 8f;
        var closeRect = new Rect(panelRect.xMax - ContentPadding - buttonSize, panelRect.y + 4f, buttonSize, buttonSize);
        DrawRoundedRect(closeRect, CloseButtonColor, ControlRadius);
        GUI.Label(closeRect, "x", _buttonLabelStyle);
        if (GUI.Button(closeRect, GUIContent.none, GUIStyle.none))
        {
            enabled = false;
        }
    }

    private void DrawSliderRow(string label, ref float value, float min, float max, string readout = null)
    {
        Rect rowRect = GUILayoutUtility.GetRect(1f, SliderRowHeight, GUILayout.ExpandWidth(true));

        var labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width, 14f);
        GUI.Label(labelRect, label, _sliderLabelStyle);

        var barRect = new Rect(rowRect.x, rowRect.y + 16f, rowRect.width, 16f);
        // Only edits the local draft - does not send or save anything by itself.
        value = DrawSlider(barRect, value, min, max, readout);
    }

    private float DrawSlider(Rect barRect, float value, float min, float max, string readout = null)
    {
        DrawRoundedRect(barRect, SliderTrackColor, barRect.height / 2f);

        float t = Mathf.InverseLerp(min, max, value);
        float fillWidth = Mathf.Max(barRect.height, barRect.width * t);
        var fillRect = new Rect(barRect.x, barRect.y, fillWidth, barRect.height);
        DrawRoundedRect(fillRect, SliderFillColor, Mathf.Min(barRect.height / 2f, fillRect.width / 2f));

        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        Event e = Event.current;
        switch (e.GetTypeForControl(controlId))
        {
            case EventType.MouseDown:
                if (barRect.Contains(e.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    e.Use();
                }

                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == controlId)
                {
                    e.Use();
                }

                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == controlId)
                {
                    GUIUtility.hotControl = 0;
                    e.Use();
                }

                break;
        }

        if (GUIUtility.hotControl == controlId && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
        {
            float newT = Mathf.InverseLerp(barRect.x, barRect.xMax, e.mousePosition.x);
            value = Mathf.Lerp(min, max, Mathf.Clamp01(newT));
        }

        GUI.Label(barRect, readout ?? $"{value * 100f:0}%", _sliderValueStyle);
        return value;
    }

    private void DrawSaveButton(Rect rect, bool enabledState)
    {
        DrawRoundedRect(rect, enabledState ? SaveButtonColor : SaveButtonDisabledColor, ControlRadius);
        GUI.Label(rect, enabledState ? "Save" : "Saved", enabledState ? _saveLabelStyle : _saveLabelDisabledStyle);

        if (enabledState && GUI.Button(rect, GUIContent.none, GUIStyle.none))
        {
            SendNow();
        }
    }

    private void SendNow()
    {
        World world = Manager.ecs != null ? Manager.ecs.ClientWorld : null;
        if (world == null || !world.IsCreated)
        {
            return;
        }

        EntityManager em = world.EntityManager;
        Entity e = em.CreateEntity();
        em.AddComponentData(e, new RequestDifficultyChangeRpc { Values = _draft });
        em.AddComponentData(e, new SendRpcCommandRequest());
    }

    private static string GetWorldModeLabel()
    {
        World world = Manager.ecs != null ? Manager.ecs.ClientWorld : null;
        if (world == null || !world.IsCreated)
        {
            return "?";
        }

        var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldInfoCD>());
        if (query.CalculateEntityCount() != 1)
        {
            return "?";
        }

        WorldInfoCD info = query.GetSingleton<WorldInfoCD>();
        if (info.IsWorldModeEnabled(WorldMode.Creative))
        {
            return "Creative";
        }

        if (info.IsWorldModeEnabled(WorldMode.Hard))
        {
            return "Hard";
        }

        if (info.IsWorldModeEnabled(WorldMode.Casual))
        {
            return "Casual";
        }

        return "Normal";
    }

    private void DrawFilledRect(Rect rect, Color color)
    {
        Color prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, _whiteTexture);
        GUI.color = prev;
    }

    private void DrawRoundedRect(Rect rect, Color color, float radius)
    {
        GUI.DrawTexture(rect, _whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, radius);
    }
}
