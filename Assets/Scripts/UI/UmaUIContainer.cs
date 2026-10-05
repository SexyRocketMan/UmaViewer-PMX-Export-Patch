using System;
using UnityEngine;
using UnityEngine.UI;

public class UmaUIContainer : MonoBehaviour
{
    enum TextType
    {
        Text,
        TextMesh,
    }

    [SerializeField]TextType TextComponentType = TextType.Text;
    public string Name
    {
        get 
        {
            switch (TextComponentType)
            {
                case TextType.Text: return Text.text;
                case TextType.TextMesh: return TextMesh.text;
                default: return TextMesh.text;
            }
        }
        set 
        {
            switch (TextComponentType)
            {
                case TextType.Text: 
                    Text.text = value;
                    break;
                case TextType.TextMesh:
                    TextMesh.text = value; 
                    break;
            }
        }
    }

    public float FontSize
    {
        get
        {
            switch (TextComponentType)
            {
                case TextType.Text: return Text.fontSize;
                case TextType.TextMesh: return TextMesh.fontSize;
                default: return TextMesh.fontSize;
            }
        }
        set
        {
            switch (TextComponentType)
            {
                case TextType.Text:
                    Text.fontSize = (int)value;
                    break;
                case TextType.TextMesh:
                    TextMesh.fontSize = value;
                    break;
            }
        }
    }
    public TMPro.TextMeshProUGUI TextMesh;
    public Text Text;
    public Button Button;
    public Slider Slider;
    public Toggle Toggle;
    public Image Image;
    public Image ToggleImage;
    public string Id;

    /// <summary>Text a search also matches, for what the row does not show: an id, another language.</summary>
    public string Keywords;

    /// <summary>True when the row's text, the keywords it was given, or its id contains the term.</summary>
    public bool Matches(string term) => Contains(Name, term) || Contains(Keywords, term) || Contains(Id, term);

    /// <summary>Case insensitive containment, shared by the rows and the entries of the paged lists.</summary>
    public static bool Contains(string text, string term) =>
        !string.IsNullOrEmpty(text) && text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
}

