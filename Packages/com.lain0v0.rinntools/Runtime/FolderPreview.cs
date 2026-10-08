using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class FolderPreview
{
    const string EnabledKey = "RinnsTools.FolderPreview.Enabled";

    // 支持的预览图文件名（按优先级从上到下依次查找）
    // Supported preview file names (searched in order, top to bottom)
    // 対応するプレビュー画像名（上から順に優先して検索されます）
    static readonly string[] PreviewNames =
    {
        "folder.jpg",
        "folder.png",
        "folder.tga",
        "folder.psd",
        "folder.tif",
        "folder.tiff",
        "folder.bmp",
        "folder.exr",
        "folder.hdr",
    };

    static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

    public static bool Enabled
    {
        get => EditorPrefs.GetBool(EnabledKey, true);   // 默认开启 / enabled by default
        set => EditorPrefs.SetBool(EnabledKey, value);
    }

    static FolderPreview()
    {
        EditorApplication.projectWindowItemOnGUI -= OnGUI;
        EditorApplication.projectWindowItemOnGUI += OnGUI;
    }

    static void OnGUI(string guid, Rect rect)
    {
        if (!Enabled) return;
        if (Event.current.type != EventType.Repaint) return;

        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (!AssetDatabase.IsValidFolder(path)) return;

        if (!Cache.TryGetValue(path, out var tex))
        {
            tex = LoadPreview(path);
            Cache[path] = tex;
        }
        if (tex == null) return;

        bool listView = rect.height <= 20f;
        Rect iconRect = listView
            ? new Rect(rect.x, rect.y, rect.height, rect.height)
            : new Rect(rect.x, rect.y, rect.width, rect.width);

        EditorGUI.DrawRect(iconRect, EditorGUIUtility.isProSkin
            ? new Color(0.22f, 0.22f, 0.22f, 1f)
            : new Color(0.76f, 0.76f, 0.76f, 1f));

        GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit, true);
    }

    // 按优先级依次尝试加载预览图，找不到返回 null
    static Texture2D LoadPreview(string folderPath)
    {
        foreach (string name in PreviewNames)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(folderPath + "/" + name);
            if (tex != null) return tex;
        }
        return null;
    }

    public static void ClearCache()
    {
        Cache.Clear();
        EditorApplication.RepaintProjectWindow();
    }
}

// 简易多语言 / 簡易多言語 / Simple localization
// 语言选择存在 EditorPrefs 里，重启后保留
// The language choice is stored in EditorPrefs and persists across restarts.
static class L
{
    const string LangKey = "RinnsTools.FolderPreview.Lang";

    public enum Lang { ZH, JA, EN }

    static Lang _current = Load();

    static Lang Load()
    {
        int v = EditorPrefs.GetInt(LangKey, -1);
        if (v >= 0 && v <= 2) return (Lang)v;
        return DetectDefault();
    }

    static Lang DetectDefault()
    {
        string sys = System.Globalization.CultureInfo.CurrentUICulture
            .TwoLetterISOLanguageName;
        if (sys == "zh") return Lang.ZH;
        if (sys == "ja") return Lang.JA;
        return Lang.EN;
    }

    public static Lang Current
    {
        get => _current;
        set
        {
            _current = value;
            EditorPrefs.SetInt(LangKey, (int)value);
        }
    }

    public static string T(string zh, string ja, string en)
    {
        switch (_current)
        {
            case Lang.ZH: return zh;
            case Lang.JA: return ja;
            default:      return en;
        }
    }
}

public class FolderPreviewSettingsWindow : EditorWindow
{
    [MenuItem("Tools/Rinn's Tools/Folder Preview Settings")]
    static void Open()
    {
        var w = GetWindow<FolderPreviewSettingsWindow>(
            L.T("文件夹预览设置", "フォルダプレビュー設定", "Folder Preview Settings"));
        w.minSize = new Vector2(420, 360);
        w.Show();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8);

        // 语言选择 / 言語選択 / Language selector
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(L.T("语言", "言語", "Language"), GUILayout.Width(60));
        EditorGUI.BeginChangeCheck();
        var newLang = (L.Lang)EditorGUILayout.EnumPopup(L.Current);
        if (EditorGUI.EndChangeCheck())
        {
            L.Current = newLang;
            titleContent = new GUIContent(
                L.T("文件夹预览设置", "フォルダプレビュー設定", "Folder Preview Settings"));
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        GUILayout.Label(L.T("文件夹预览", "フォルダプレビュー", "Folder Preview"),
            EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        EditorGUI.BeginChangeCheck();
        bool enabled = EditorGUILayout.ToggleLeft(
            L.T("启用文件夹预览", "フォルダプレビューを有効にする", "Enable Folder Preview"),
            FolderPreview.Enabled);
        if (EditorGUI.EndChangeCheck())
        {
            FolderPreview.Enabled = enabled;
            FolderPreview.ClearCache();
        }

        EditorGUILayout.Space(8);

        if (GUILayout.Button(
            L.T("刷新预览缓存", "プレビューキャッシュを更新", "Refresh Preview Cache")))
            FolderPreview.ClearCache();

        EditorGUILayout.Space(10);

        EditorGUILayout.HelpBox(
            L.T(
                "使用说明：\n\n" +
                "1. 在需要预览图的文件夹里放一张名为 folder 的图片，支持以下格式：\n" +
                "    jpg / png / tga / psd / tif / tiff / bmp / exr / hdr\n" +
                "2. 同一个文件夹里有多张时，按上面列出的顺序优先取用（jpg 优先级最高）。\n" +
                "3. 项目窗口会自动用这张图替换 Unity 默认的文件夹图标。\n" +
                "4. 更换图片后如果没立刻刷新，点上面的“刷新预览缓存”。\n" +
                "5. 取消勾选上面的开关即可停用本功能，设置会被记住。\n" +
                "6. 恢复启用不需要重启 Unity。",

                "使い方：\n\n" +
                "1. プレビュー画像を表示したいフォルダ内に、folder という名前の画像を置きます。\n" +
                "    対応形式：jpg / png / tga / psd / tif / tiff / bmp / exr / hdr\n" +
                "2. 同じフォルダ内に複数ある場合は、上記の順番で優先して使用されます（jpg が最優先）。\n" +
                "3. Project ウィンドウのフォルダアイコンが自動的にその画像に置き換わります。\n" +
                "4. 画像を差し替えてもすぐ反映されない場合は、上の「プレビューキャッシュを更新」を押してください。\n" +
                "5. 上のトグルをオフにすると本機能を無効にできます。設定は保存されます。\n" +
                "6. 再有効化に Unity の再起動は不要です。",

                "How to use:\n\n" +
                "1. Put an image named \"folder\" inside the folder you want to preview.\n" +
                "    Supported formats: jpg / png / tga / psd / tif / tiff / bmp / exr / hdr\n" +
                "2. If several are present, the first match in the list above wins (jpg has the highest priority).\n" +
                "3. The Project window will automatically use it to replace Unity's default folder icon.\n" +
                "4. If a changed image doesn't refresh immediately, click \"Refresh Preview Cache\" above.\n" +
                "5. Uncheck the toggle above to disable the feature. The setting is remembered.\n" +
                "6. Re-enabling does not require restarting Unity."
            ),
            MessageType.Info);
    }
}