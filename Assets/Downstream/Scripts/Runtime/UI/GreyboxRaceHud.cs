using Downstream.Cameras;
using Downstream.Core.Items;
using Downstream.Core.Race;
using Downstream.Race;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Downstream.UI
{
    /// <summary>
    /// Greybox HUD drawn with IMGUI so the race is playable before the UI Toolkit art pass. Follows the
    /// design's HUD layout: place top left, item and held-behind slots top right, boost and drift tiers
    /// bottom centre, and the shared source-to-mouth river strip on the right edge. Shows the countdown,
    /// the Kingfisher warning, respawns and the results table with a one-press rematch.
    /// </summary>
    public sealed class GreyboxRaceHud : MonoBehaviour
    {
        [SerializeField] private RaceDirector _director;

        private GUIStyle _big, _normal, _small;
        private Texture2D _white;
        private float _goShownUntil;
        private RaceSession _lastRace;

        private static readonly Color[] BoatColours =
        {
            new Color(0.95f, 0.45f, 0.25f), new Color(0.3f, 0.6f, 0.95f), new Color(0.95f, 0.85f, 0.3f), new Color(0.6f, 0.85f, 0.4f),
            new Color(0.75f, 0.5f, 0.9f), new Color(0.4f, 0.85f, 0.85f), new Color(0.9f, 0.55f, 0.7f), new Color(0.8f, 0.8f, 0.8f),
        };

        private void Reset() => _director = GetComponent<RaceDirector>();

        private void Update()
        {
            var race = _director != null ? _director.Race : null;
            if (race == null) return;

            if (race != _lastRace)
            {
                _lastRace = race;
                _goShownUntil = 0f;
            }
            if ((_director.FrameRaceEvents(0) & RaceEvents.Go) != 0) _goShownUntil = Time.time + 1f;

            if (race.Phase == RacePhase.Finished && RematchPressed()) _director.Rematch();
        }

        private static bool RematchPressed()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) return true;
            foreach (var pad in Gamepad.all)
                if (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame) return true;
            return false;
        }

        private void OnGUI()
        {
            var race = _director != null ? _director.Race : null;
            if (race == null) return;
            EnsureStyles();

            int players = Mathf.Max(1, _director.HumanCount);
            float hud = SplitScreenLayout.HudScale(players);
            for (int p = 0; p < players; p++)
            {
                var vp = SplitScreenLayout.ViewportFor(p, players);
                // Camera viewports grow up from the bottom; GUI rectangles grow down from the top.
                var rect = new Rect(vp.x * Screen.width, (1f - vp.y - vp.height) * Screen.height, vp.width * Screen.width, vp.height * Screen.height);
                DrawPlayer(race, p, rect, hud);
            }

            DrawRiverStrip(race);
            if (race.Phase == RacePhase.Finished) DrawResults(race);
        }

        private void DrawPlayer(RaceSession race, int boat, Rect area, float scale)
        {
            float pad = 24f * Ui;
            var status = race.Status[boat];
            var state = race.Sim.State.Boats[boat];
            var items = race.Items != null ? race.Items.Boats[boat] : default;

            // Place, top left.
            Label(new Rect(area.x + pad, area.y + pad, 400f * Ui, 120f * Ui), $"{Ordinal(status.Place)}", _big, scale);
            Label(new Rect(area.x + pad, area.y + pad + 110f * Ui * scale, 400f * Ui, 50f * Ui), $"of {race.Sim.BoatCount}", _small, scale);

            // Item slot and held-behind slot, top right (leaving room for the river strip).
            float right = area.xMax - pad - StripWidth - 340f * Ui;
            string held = items.Held == ItemType.None ? "-" : Name(items.Held) + (items.Uses > 1 ? $" x{items.Uses}" : "");
            string behind = items.Behind == ItemType.None ? "" : $"behind: {Name(items.Behind)}";
            Box(new Rect(right, area.y + pad, 320f * Ui * scale, 110f * Ui * scale), new Color(0f, 0f, 0f, 0.45f));
            Label(new Rect(right + 12f, area.y + pad, 320f * Ui, 60f * Ui), held, _normal, scale);
            Label(new Rect(right + 12f, area.y + pad + 54f * Ui * scale, 320f * Ui, 50f * Ui), behind, _small, scale);
            if (items.ShieldTime > 0f) Label(new Rect(right, area.y + pad + 120f * Ui * scale, 320f * Ui, 50f * Ui), $"Shield {items.ShieldTime:F1}s", _small, scale);
            if (items.KingfisherIncoming > 0f)
                Label(new Rect(area.center.x - 300f * Ui, area.y + area.height * 0.22f, 600f * Ui, 80f * Ui),
                    $"KINGFISHER {items.KingfisherIncoming:F1}  -  HOP!", _normal, scale, new Color(1f, 0.35f, 0.25f));

            // Boost and drift tiers, bottom centre.
            float barW = 360f * Ui * scale, barH = 26f * Ui * scale;
            float bx = area.center.x - barW * 0.5f, by = area.yMax - pad - barH;
            for (int k = 0; k < 3; k++)
            {
                var seg = new Rect(bx + k * (barW / 3f) + 3f, by, barW / 3f - 6f, barH);
                Box(seg, new Color(0f, 0f, 0f, 0.45f));
                if (state.DriftTier > k) Box(seg, k == 0 ? new Color(0.4f, 0.75f, 1f) : k == 1 ? new Color(1f, 0.6f, 0.2f) : new Color(0.85f, 0.4f, 1f));
            }
            if (state.BoostTime > 0f)
                Label(new Rect(bx, by - 54f * Ui * scale, barW, 50f * Ui), "BOOST", _normal, scale, new Color(0.6f, 0.95f, 1f), TextAnchor.MiddleCenter);
            if (state.InHole)
                Label(new Rect(bx, by - 104f * Ui * scale, barW, 50f * Ui), "IN THE HOLE - HOP!", _small, scale, Color.white, TextAnchor.MiddleCenter);

            // Countdown, respawn and finish messages, centre.
            var centre = new Rect(area.center.x - 400f * Ui, area.center.y - 90f * Ui, 800f * Ui, 180f * Ui);
            if (race.Phase == RacePhase.Countdown)
                Label(centre, race.CountdownNumber.ToString(), _big, scale * 1.5f, Color.white, TextAnchor.MiddleCenter);
            else if (Time.time < _goShownUntil)
                Label(centre, "GO!", _big, scale * 1.5f, new Color(0.6f, 1f, 0.6f), TextAnchor.MiddleCenter);
            else if (status.IsRespawning)
                Label(centre, "Back on the river...", _normal, scale, Color.white, TextAnchor.MiddleCenter);
            else if (status.Finished && race.Phase != RacePhase.Finished)
                Label(centre, $"Finished {Ordinal(status.FinishPlace)}  {FormatTime(status.FinishTime)}", _normal, scale, Color.white, TextAnchor.MiddleCenter);
        }

        private const float StripWidthBase = 28f;
        private float StripWidth => StripWidthBase * Ui;

        /// <summary>A vertical source-to-mouth ribbon with every boat, the three zones and the flood front.</summary>
        private void DrawRiverStrip(RaceSession race)
        {
            float pad = 24f * Ui;
            var strip = new Rect(Screen.width - pad - StripWidth, pad * 2f, StripWidth, Screen.height - pad * 4f);
            Box(strip, new Color(0.08f, 0.2f, 0.3f, 0.7f));
            for (int z = 1; z < 3; z++)
                Box(new Rect(strip.x - 4f, strip.y + strip.height * z / 3f, strip.width + 8f, 2f), new Color(1f, 1f, 1f, 0.5f));

            float length = race.Sim.Track.Length;
            var water = race.Sim.Water as Core.Water.RiverWater;
            if (water != null)
            {
                foreach (var flood in water.Layers.Floods)
                {
                    float front = flood.StartDistance + flood.Speed * (race.Sim.RaceTime - flood.StartTime);
                    if (race.Sim.RaceTime < flood.StartTime || front > length) continue;
                    float fy = strip.y + strip.height * Mathf.Clamp01(front / length);
                    Box(new Rect(strip.x, strip.y, strip.width, fy - strip.y), new Color(0.55f, 0.4f, 0.2f, 0.45f));
                }
            }

            for (int i = race.Sim.BoatCount - 1; i >= 0; i--)
            {
                float progress = race.Status[i].Finished ? length : race.Sim.State.Progress[i];
                float y = strip.y + strip.height * Mathf.Clamp01(progress / length);
                bool human = i < _director.HumanCount;
                float size = (human ? 18f : 12f) * Ui;
                Box(new Rect(strip.center.x - size * 0.5f, y - size * 0.5f, size, size), BoatColours[i % BoatColours.Length]);
            }
        }

        private void DrawResults(RaceSession race)
        {
            var results = race.Results();
            float w = 720f * Ui, row = 46f * Ui;
            var panel = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.15f, w, row * (results.Length + 3));
            Box(panel, new Color(0f, 0f, 0f, 0.7f));
            Label(new Rect(panel.x, panel.y + 8f, w, row), "Results", _normal, 1f, Color.white, TextAnchor.MiddleCenter);
            for (int k = 0; k < results.Length; k++)
            {
                var r = results[k];
                string who = r.Boat < _director.HumanCount ? $"Player {r.Boat + 1}" : $"Rival {r.Boat}";
                string time = r.Finished ? FormatTime(r.Time) : "DNF";
                var line = new Rect(panel.x + 30f * Ui, panel.y + row * (k + 1.2f), w - 60f * Ui, row);
                Label(line, $"{Ordinal(r.Place),-5} {who,-12} {time}", _small, 1f, r.Boat < _director.HumanCount ? new Color(1f, 0.85f, 0.4f) : Color.white);
            }
            Label(new Rect(panel.x, panel.yMax - row * 1.2f, w, row), "Enter / A: rematch", _small, 1f, new Color(0.8f, 0.9f, 1f), TextAnchor.MiddleCenter);
        }

        // ---- helpers ------------------------------------------------------------------------------

        /// <summary>Scale so text is never under 24 px at 1080p (design: UI/UX acceptance).</summary>
        private static float Ui => Mathf.Max(0.5f, Screen.height / 1080f);

        private void EnsureStyles()
        {
            if (_big != null) return;
            _white = Texture2D.whiteTexture;
            _big = new GUIStyle(GUI.skin.label) { fontSize = 96, fontStyle = FontStyle.Bold };
            _normal = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 28 };
        }

        private void Label(Rect r, string text, GUIStyle style, float scale, Color? colour = null, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            int size = style.fontSize;
            var align = style.alignment;
            style.fontSize = Mathf.Max(Mathf.RoundToInt(24f * Ui), Mathf.RoundToInt(size * scale * Ui));
            style.alignment = anchor;
            var old = GUI.color;
            // A one-pixel shadow keeps text readable on bright water.
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
            GUI.color = colour ?? Color.white;
            GUI.Label(r, text, style);
            GUI.color = old;
            style.fontSize = size;
            style.alignment = align;
        }

        private void Box(Rect r, Color colour)
        {
            var old = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }

        private static string Ordinal(int n)
        {
            if (n <= 0) return "-";
            int t = n % 100;
            string suffix = t >= 11 && t <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
            return n + suffix;
        }

        private static string FormatTime(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int m = (int)(seconds / 60f);
            return $"{m}:{seconds - m * 60f:00.00}";
        }

        private static string Name(ItemType item)
        {
            switch (item)
            {
                case ItemType.LilyMine: return "Lily Mine";
                case ItemType.LogJam: return "Log Jam";
                case ItemType.ReedShield: return "Reed Shield";
                case ItemType.TurbineX1: return "Turbine";
                case ItemType.TurbineX3: return "Turbine";
                case ItemType.TriplePike: return "Triple Pike";
                case ItemType.WakeBlaster: return "Wake Blaster";
                case ItemType.DamBurst: return "Dam Burst";
                default: return item.ToString();
            }
        }
    }
}
