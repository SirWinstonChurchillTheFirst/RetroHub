using Avalonia.Media;

namespace RetroHub.App.Theming;

/// <summary>
/// The font roles of "Fernsehlicht" (DESIGN 3.1) for code that draws text itself (placeholders, spines, OSD).
/// Same family strings as the RhFont* resources in Themes/Tokens.axaml; one folder per Archivo width (a shared
/// folder makes Avalonia 12.1.5 resolve every width to Condensed). FontRoleTests guards both.
/// </summary>
public static class FontRoles
{
    public const string PosterUri = "avares://RetroHub.App/Assets/Fonts/ArchivoExpanded#Archivo Expanded";
    public const string NumberUri = "avares://RetroHub.App/Assets/Fonts/ArchivoSemiExpanded#Archivo SemiExpanded";
    public const string UiUri = "avares://RetroHub.App/Assets/Fonts/ArchivoSemiCondensed#Archivo SemiCondensed";
    public const string SpineUri = "avares://RetroHub.App/Assets/Fonts/ArchivoCondensed#Archivo Condensed";
    public const string ReadUri = "avares://RetroHub.App/Assets/Fonts/Atkinson#Atkinson Hyperlegible Next";
    private const string Fallback = ", fonts:Inter#Inter";

    public static readonly FontFamily Poster = new(PosterUri + Fallback);
    public static readonly FontFamily Number = new(NumberUri + Fallback);
    public static readonly FontFamily Ui = new(UiUri + Fallback);
    public static readonly FontFamily Spine = new(SpineUri + Fallback);
    public static readonly FontFamily Read = new(ReadUri + Fallback);

    public static readonly Typeface PosterFace = new(Poster, FontStyle.Normal, FontWeight.ExtraBold);
    public static readonly Typeface NumberFace = new(Number, FontStyle.Normal, FontWeight.Bold);
    public static readonly Typeface UiFace = new(Ui, FontStyle.Normal, FontWeight.Medium);
    public static readonly Typeface LabelFace = new(Ui, FontStyle.Normal, FontWeight.SemiBold);
    public static readonly Typeface SpineFace = new(Spine, FontStyle.Normal, FontWeight.Bold);
    public static readonly Typeface ReadFace = new(Read, FontStyle.Normal, FontWeight.Normal);
}
