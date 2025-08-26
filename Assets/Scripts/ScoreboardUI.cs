using UnityEngine;
using TMPro;

public class ScoreboardUI : MonoBehaviour
{
    public static ScoreboardUI Instance;
    public TMP_Text text;

    void Awake() => Instance = this;
    public void SetText(string s) { if (text) text.text = s; }
}