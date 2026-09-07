using UnityEngine;

namespace RoboStreetSoccer
{
    public sealed class SoccerHud : MonoBehaviour
    {
        SoccerGame game;
        GUIStyle score;
        GUIStyle label;
        GUIStyle panel;
        GUIStyle button;
        GUIStyle prompt;

        void Start() => game = FindAnyObjectByType<SoccerGame>();

        void BuildStyles()
        {
            score = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            label = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = new Color(.91f, .98f, .96f) }, wordWrap = true };
            panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 9, 9), normal = { background = MakeTexture(new Color(.025f, .09f, .11f, .88f)) } };
            button = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, fixedHeight = 28 };
            prompt = new GUIStyle(score) { fontSize = 19 };
        }

        Texture2D MakeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        void OnGUI()
        {
            if (!game) return;
            if (score == null) BuildStyles();
            GUILayout.BeginArea(new Rect(Screen.width * .5f - 230f, 12f, 460f, 52f), panel);
            GUILayout.Label($"ORANGE  {game.OrangeScore}     —     {game.MintScore}  MINT", score);
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(16f, 14f, 360f, 252f), panel);
            GUILayout.Label($"{game.StatusText}   •   {game.CurrentSpeed:0.00}×", label);
            GUILayout.Label($"Controls: {game.Controls}   •   Opponents: {game.Difficulty}   •   Selected: {(game.SelectedRobot ? game.SelectedRobot.name : "none")}", label);
            GUILayout.Label(game.Controls == ControlPreset.Beginner
                ? "Arrows/WASD move  •  Space pass  •  J shoot  •  close facing tackles are assisted"
                : "Arrows/WASD move  •  Space pass/switch  •  J/left-click shoot/tackle", label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1×", button)) game.SetSpeed(1f);
            if (GUILayout.Button("½×", button)) game.SetSpeed(.5f);
            if (GUILayout.Button("¼×", button)) game.SetSpeed(.25f);
            if (GUILayout.Button(game.State == MatchState.Paused ? "Resume" : "Pause", button)) game.TogglePause();
            if (GUILayout.Button("Restart", button)) game.ResetMatch();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Control: " + game.Controls, button)) game.ToggleControls();
            if (GUILayout.Button("Opponent: " + game.Difficulty, button)) game.ToggleDifficulty();
            GUILayout.EndHorizontal();
            if (game.Controls == ControlPreset.Beginner && GUILayout.Button("SHOOT  [J]", button) && game.SelectedRobot)
                game.SelectedRobot.RequestShoot();
            GUILayout.Label("Camera: right-drag orbit  •  wheel or +/- zoom\nQ/E rotate  •  Home reset", label);
            GUILayout.EndArea();

            if (game.State == MatchState.KickInSetup)
            {
                GUILayout.BeginArea(new Rect(Screen.width * .5f - 175f, 70f, 350f, 42f), panel);
                GUILayout.Label(game.KickInTeam == SoccerTeam.Orange ? "KICK-IN — PRESS SPACE TO PASS" : "MINT KICK-IN", prompt);
                GUILayout.EndArea();
            }
            else if (game.State == MatchState.MatchOver)
            {
                GUILayout.BeginArea(new Rect(Screen.width * .5f - 190f, 70f, 380f, 70f), panel);
                GUILayout.Label(game.StatusText, prompt);
                if (GUILayout.Button("PLAY AGAIN", button)) game.ResetMatch();
                GUILayout.EndArea();
            }

            if (game.DiagnosticsVisible)
            {
                GUILayout.BeginArea(new Rect(Screen.width - 400f, 14f, 384f, 185f), panel);
                GUILayout.Label($"State {game.State} • fixed {Time.fixedDeltaTime:0.000}s • ball {game.Ball.transform.position} • {game.Ball.Body.linearVelocity.magnitude:0.00} m/s", label);
                int start = Mathf.Max(0, game.Events.Count - 6);
                for (int i = start; i < game.Events.Count; i++)
                {
                    DiagnosticEvent item = game.Events[i];
                    GUILayout.Label($"{item.simulationTime:0.00} {item.type}: {item.actor} → {item.target}", label);
                }
                GUILayout.EndArea();
            }
        }
    }
}
