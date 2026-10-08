using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class BoneAdjustmentWindow : EditorWindow
{
    public Transform sourceAvatar;
    public Transform targetClothing;

    // ============================== 语言 ==============================

    public enum UILanguage { Chinese, English, Japanese }
    public UILanguage uiLanguage = UILanguage.Chinese;

    /// <summary>三语文本。未提供日文时自动回退到英文。</summary>
    private string T(string zh, string en, string jp = null)
    {
        switch (uiLanguage)
        {
            case UILanguage.Chinese:  return zh;
            case UILanguage.Japanese: return jp ?? en;
            case UILanguage.English:
            default:                  return en;
        }
    }

    // ============================== 操作类型 ==============================

    public enum ActionType
    {
        MoveLocal,
        SetLocalPosition,
        CopyWorldPosition,
        AddLocalEuler,
        SetLocalEuler,
        CopyWorldRotation,
        SetLocalScale,
        MultiplyLocalScale,
        CopyWorldScale,
    }

    [System.Serializable]
    public class Rule
    {
        public string boneName;
        public ActionType action;
        public Vector3 value;
        public Vector3 axisMask = Vector3.one;
        public string sourceBoneName;
        public bool enabled = true;

        public Transform target;
        public Transform source;

        public Rule() { }

        public Rule(string boneName, ActionType action, Vector3 value,
                    Vector3? axisMask = null, string sourceBoneName = "")
        {
            this.boneName = boneName;
            this.action = action;
            this.value = value;
            this.axisMask = axisMask ?? Vector3.one;
            this.sourceBoneName = sourceBoneName;
            this.enabled = true;
        }

        public bool NeedsSource()
        {
            return action == ActionType.CopyWorldPosition
                || action == ActionType.CopyWorldRotation
                || action == ActionType.CopyWorldScale;
        }
    }

    [System.Serializable]
    public class Profile
    {
        public string name;
        public List<Rule> rules = new List<Rule>();

        public Profile() { }
        public Profile(string name) { this.name = name; }

        public Profile Duplicate(string copySuffix)
        {
            var p = new Profile(name + copySuffix);
            foreach (var r in rules)
            {
                var nr = new Rule(r.boneName, r.action, r.value, r.axisMask, r.sourceBoneName);
                nr.enabled = r.enabled;
                nr.target = r.target;
                nr.source = r.source;
                p.rules.Add(nr);
            }
            return p;
        }
    }

    // ============================== 序列化 DTO（用于 EditorPrefs 持久化）==============================

    [System.Serializable]
    private class RuleSave
    {
        public string boneName;
        public ActionType action;
        public Vector3 value;
        public Vector3 axisMask;
        public string sourceBoneName;
        public bool enabled;
    }

    [System.Serializable]
    private class ProfileSave
    {
        public string name;
        public List<RuleSave> rules = new List<RuleSave>();
    }

    [System.Serializable]
    private class ProfileListSave
    {
        public int currentProfileIndex;
        public List<ProfileSave> profiles = new List<ProfileSave>();
    }

    // ============================== 内部状态 ==============================

    private const string PrefsKey      = "BoneAdjustmentWindow.Profiles.v1";
    private const string PrefsLangKey  = "BoneAdjustmentWindow.Language.v1";

    public List<Profile> profiles = new List<Profile>();
    public int currentProfileIndex = 0;

    private class TransformState
    {
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }
    private readonly Dictionary<Transform, TransformState> snapshot = new Dictionary<Transform, TransformState>();
    private bool isPreviewing = false;

    private Vector2 scrollPos;

    // 说明面板默认折叠
    private bool showActionHelp = false;

    // ============================== 入口 ==============================

    [MenuItem("Tools/Rinn's Tools/Clothing Adjustment")]
    public static void ShowWindow()
    {
        GetWindow<BoneAdjustmentWindow>("Clothing Adjustment");
    }

    private Profile CurrentProfile
    {
        get
        {
            if (profiles == null || profiles.Count == 0) return null;
            currentProfileIndex = Mathf.Clamp(currentProfileIndex, 0, profiles.Count - 1);
            return profiles[currentProfileIndex];
        }
    }

    private void OnEnable()
    {
        // 先读语言，再生成默认预设（这样默认名也会用正确的语言）
        uiLanguage = (UILanguage)EditorPrefs.GetInt(PrefsLangKey, (int)UILanguage.Chinese);

        if (profiles == null) profiles = new List<Profile>();

        if (!LoadProfiles())
        {
            if (profiles.Count == 0)
            {
                profiles.Add(new Profile(T("默认", "Default", "デフォルト")));
                currentProfileIndex = 0;
            }
        }
    }

    private void OnDisable()
    {
        if (isPreviewing) RestorePreview();
    }

    // ============================== 保存 / 加载 ==============================

    private void SaveProfiles()
    {
        var save = new ProfileListSave();
        save.currentProfileIndex = currentProfileIndex;

        foreach (var p in profiles)
        {
            var ps = new ProfileSave { name = p.name };
            foreach (var r in p.rules)
            {
                ps.rules.Add(new RuleSave
                {
                    boneName       = r.boneName,
                    action         = r.action,
                    value          = r.value,
                    axisMask       = r.axisMask,
                    sourceBoneName = r.sourceBoneName,
                    enabled        = r.enabled,
                });
            }
            save.profiles.Add(ps);
        }

        string json = JsonUtility.ToJson(save, true);
        EditorPrefs.SetString(PrefsKey, json);
        EditorPrefs.SetInt(PrefsLangKey, (int)uiLanguage);

        EditorUtility.DisplayDialog(
            T("保存成功", "Save Successful", "保存完了"),
            T("预设已保存。", "Profiles saved.", "プリセットを保存しました。"),
            "OK");
    }

    private bool LoadProfiles()
    {
        string json = EditorPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(json)) return false;

        try
        {
            var save = JsonUtility.FromJson<ProfileListSave>(json);
            if (save == null || save.profiles == null || save.profiles.Count == 0)
                return false;

            profiles = new List<Profile>();
            foreach (var ps in save.profiles)
            {
                var p = new Profile(ps.name);
                foreach (var rs in ps.rules)
                {
                    var r = new Rule(rs.boneName, rs.action, rs.value, rs.axisMask, rs.sourceBoneName);
                    r.enabled = rs.enabled;
                    p.rules.Add(r);
                }
                profiles.Add(p);
            }
            currentProfileIndex = Mathf.Clamp(save.currentProfileIndex, 0, profiles.Count - 1);
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Clothing Adjustment] 读取预设失败 / Failed to load profiles: " + e.Message);
            return false;
        }
    }

    // ============================== 参考配置 ==============================

    private Profile CreateReferenceProfile()
    {
        var p = new Profile(T("参考设置", "Reference", "参考設定"));

        // ── Hips ──
        p.rules.Add(new Rule("Hips", ActionType.CopyWorldPosition, Vector3.zero, null, "Hips"));
        p.rules.Add(new Rule("Hips", ActionType.CopyWorldRotation, Vector3.zero, null, "Hips"));

        // ── Arms ──
        p.rules.Add(new Rule("UpperArm.L", ActionType.SetLocalScale, new Vector3(0.845f, 0.845f, 0.845f)));
        p.rules.Add(new Rule("UpperArm.R", ActionType.SetLocalScale, new Vector3(0.845f, 0.855f, 0.845f)));
        p.rules.Add(new Rule("LowerArm.L", ActionType.SetLocalScale, new Vector3(1.000f, 0.990f, 1.000f)));
        p.rules.Add(new Rule("LowerArm.R", ActionType.SetLocalScale, new Vector3(1.000f, 0.990f, 1.000f)));

        // ── Neck / Head ──
        p.rules.Add(new Rule("Neck", ActionType.CopyWorldPosition, Vector3.zero, null, "Neck"));
        p.rules.Add(new Rule("Neck", ActionType.CopyWorldRotation, Vector3.zero, null, "Neck"));
        p.rules.Add(new Rule("Neck", ActionType.SetLocalScale, new Vector3(0.855f, 0.855f, 0.855f)));
        p.rules.Add(new Rule("Head", ActionType.CopyWorldPosition, Vector3.zero, null, "Head"));
        p.rules.Add(new Rule("Head", ActionType.CopyWorldRotation, Vector3.zero, null, "Head"));

        // ── UpperLegs ──
        p.rules.Add(new Rule("UpperLeg.L", ActionType.SetLocalScale, new Vector3(0.92f, 0.853f, 0.853f)));
        p.rules.Add(new Rule("UpperLeg.R", ActionType.SetLocalScale, new Vector3(0.92f, 0.853f, 0.853f)));

        // ── LowerLegs ──
        p.rules.Add(new Rule("LowerLeg.L", ActionType.SetLocalEuler, new Vector3(1.8f, 0, 0), new Vector3(1, 0, 0)));
        p.rules.Add(new Rule("LowerLeg.L", ActionType.SetLocalScale, new Vector3(1, 0.955f, 1), new Vector3(0, 1, 0)));
        p.rules.Add(new Rule("LowerLeg.R", ActionType.SetLocalEuler, new Vector3(1.8f, 0, 0), new Vector3(1, 0, 0)));
        p.rules.Add(new Rule("LowerLeg.R", ActionType.SetLocalScale, new Vector3(1, 0.955f, 1), new Vector3(0, 1, 0)));

        // ── Feet ──
        p.rules.Add(new Rule("Foot.L", ActionType.CopyWorldPosition, Vector3.zero, null, "Foot.L"));
        p.rules.Add(new Rule("Foot.L", ActionType.CopyWorldRotation, Vector3.zero, null, "Foot.L"));
        p.rules.Add(new Rule("Foot.L", ActionType.MultiplyLocalScale, new Vector3(1.1f, 1.135f, 1.135f)));
        p.rules.Add(new Rule("Foot.R", ActionType.CopyWorldPosition, Vector3.zero, null, "Foot.R"));
        p.rules.Add(new Rule("Foot.R", ActionType.CopyWorldRotation, Vector3.zero, null, "Foot.R"));
        p.rules.Add(new Rule("Foot.R", ActionType.MultiplyLocalScale, new Vector3(1.1f, 1.135f, 1.135f)));

        // ── Toes ──
        p.rules.Add(new Rule("Toe.L", ActionType.CopyWorldPosition, Vector3.zero, null, "Toe.L"));
        p.rules.Add(new Rule("Toe.L", ActionType.CopyWorldRotation, Vector3.zero, null, "Toe.L"));
        p.rules.Add(new Rule("Toe.R", ActionType.CopyWorldPosition, Vector3.zero, null, "Toe.R"));
        p.rules.Add(new Rule("Toe.R", ActionType.CopyWorldRotation, Vector3.zero, null, "Toe.R"));

        return p;
    }

    // ============================== 说明文字 ==============================

    private string GetActionTooltip(ActionType a)
    {
        switch (a)
        {
            case ActionType.MoveLocal:
                return T(
                    "增量位移：在当前 localPosition 基础上【加上】填写的值。",
                    "Delta move: ADDS the value to the current localPosition.",
                    "増分移動：現在の localPosition に値を【加算】します。");

            case ActionType.SetLocalPosition:
                return T(
                    "绝对值位移：把 localPosition 【直接设为】填写的值，无视原值。",
                    "Absolute move: SETS localPosition to the value, ignoring the original.",
                    "絶対移動：localPosition を値に【直接設定】します（元の値を無視）。");

            case ActionType.CopyWorldPosition:
                return T(
                    "复制世界位置：把目标的【世界坐标】对齐到源骨骼（自动换算回本地空间）。",
                    "Copy world position: aligns this bone's WORLD position to the source bone.",
                    "ワールド位置コピー：対象の【ワールド座標】をソースボーンに合わせます（ローカル空間に換算）。");

            case ActionType.AddLocalEuler:
                return T(
                    "增量旋转：在当前 localEulerAngles 基础上【加上】填写的角度。",
                    "Delta rotation: ADDS the value to the current localEulerAngles.",
                    "増分回転：現在の localEulerAngles に角度を【加算】します。");

            case ActionType.SetLocalEuler:
                return T(
                    "绝对旋转：把 localEulerAngles 【直接设为】填写的角度。",
                    "Absolute rotation: SETS localEulerAngles to the value.",
                    "絶対回転：localEulerAngles を角度に【直接設定】します。");

            case ActionType.CopyWorldRotation:
                return T(
                    "复制世界旋转：把目标的【世界旋转】对齐到源骨骼（自动换算回本地空间）。",
                    "Copy world rotation: aligns this bone's WORLD rotation to the source bone.",
                    "ワールド回転コピー：対象の【ワールド回転】をソースボーンに合わせます（ローカル空間に換算）。");

            case ActionType.SetLocalScale:
                return T(
                    "绝对缩放：把 localScale 【直接设为】填写的值。",
                    "Absolute scale: SETS localScale to the value.",
                    "絶対スケール：localScale を値に【直接設定】します。");

            case ActionType.MultiplyLocalScale:
                return T(
                    "相对缩放：localScale 【乘以】填写的值（在原缩放基础上按比例变化）。",
                    "Relative scale: MULTIPLIES localScale by the value.",
                    "相対スケール：localScale に値を【乗算】します（元のスケールに対して比例変化）。");

            case ActionType.CopyWorldScale:
                return T(
                    "复制世界缩放：把目标的【世界缩放】对齐到源骨骼（按父级 lossyScale 换算回本地）。",
                    "Copy world scale: aligns this bone's WORLD scale to the source bone.",
                    "ワールドスケールコピー：対象の【ワールドスケール】をソースボーンに合わせます（親の lossyScale で換算）。");
        }
        return "";
    }

    private void DrawActionHelpContent()
    {
        DrawHelpHeader(T("轴掩码", "Axis Mask", "軸マスク"));
        DrawHelpBody(T(
            "只勾选想修改的轴，未勾选的轴保持原值不变。\n" +
            "例：只想改 Y 轴缩放 → 用 MultiplyLocalScale，值填 (1, 0.95, 1)，只勾选 Y。",
            "Only checked axes are modified; unchecked axes keep their original value.\n" +
            "Example: scale Y only → use MultiplyLocalScale with (1, 0.95, 1) and check Y only.",
            "チェックした軸のみ変更され、未チェックの軸は元の値を保持します。\n" +
            "例：Y 軸スケールのみ変更 → MultiplyLocalScale に (1, 0.95, 1) を設定し Y のみチェック。"));

        EditorGUILayout.Space(6);

        DrawHelpHeader(T("操作类型（位移）", "Action Types - Position", "操作タイプ（位置）"));

        DrawHelpEntry(T("MoveLocal  —— 位移（增量）", "MoveLocal  —— position (delta)", "MoveLocal  —— 位置（増分）"),
            T("在当前 localPosition 的基础上【加上】填写的值。\n" +
              "例：原 localPosition = (0, 1, 0)，值 = (0, 0.1, 0) → 结果是 (0, 1.1, 0)。",
              "ADDS the value onto the current localPosition.\n" +
              "Ex: localPosition (0,1,0) + value (0,0.1,0) → (0,1.1,0).",
              "現在の localPosition に値を【加算】します。\n" +
              "例：localPosition (0,1,0) + 値 (0,0.1,0) → (0,1.1,0)。"));

        DrawHelpEntry(T("SetLocalPosition  —— 位移（绝对值）", "SetLocalPosition  —— position (absolute)", "SetLocalPosition  —— 位置（絶対値）"),
            T("把 localPosition 【直接设为】填写的值，与原来的位置无关。\n" +
              "例：不管原值是多少，值 = (0, 1, 0) → 结果一定是 (0, 1, 0)。",
              "SETS localPosition to the value, ignoring the original.\n" +
              "Ex: value (0,1,0) → result is always (0,1,0).",
              "localPosition を値に【直接設定】します（元の位置を無視）。\n" +
              "例：値 (0,1,0) → 結果は常に (0,1,0)。"));

        DrawHelpEntry(T("CopyWorldPosition  —— 复制世界位置", "CopyWorldPosition  —— copy world position", "CopyWorldPosition  —— ワールド位置コピー"),
            T("把目标的【世界坐标】对齐到源骨骼的世界坐标（自动换算回本地空间）。\n" +
              "需要填「源骨骼」名字，且会随源骨骼移动一起变化。\n" +
              "典型用途：让衣服的 Hips / Foot / Toe 精准贴合原模型。",
              "Aligns this bone's WORLD position to the source bone (converted back to local space).\n" +
              "Needs a Source Bone. Follows the source bone when it moves.\n" +
              "Typical use: snap Hips / Foot / Toe of the clothing onto the avatar.",
              "対象の【ワールド座標】をソースボーンのワールド座標に合わせます（ローカル空間に換算）。\n" +
              "「ソースボーン」名が必要で、ソースボーンの移動にも追従します。\n" +
              "用途：衣装の Hips / Foot / Toe を元モデルにぴったり合わせる。"));

        EditorGUILayout.Space(6);

        DrawHelpHeader(T("操作类型（旋转）", "Action Types - Rotation", "操作タイプ（回転）"));

        DrawHelpEntry(T("AddLocalEuler  —— 旋转（增量）", "AddLocalEuler  —— rotation (delta)", "AddLocalEuler  —— 回転（増分）"),
            T("在当前 localEulerAngles 的基础上【加上】填写的角度。\n" +
              "例：原 (0, 0, 0)，值 = (1.8, 0, 0) → 结果是 (1.8, 0, 0)。\n" +
              "注意：欧拉角相加时配合轴掩码只改单轴最安全。",
              "ADDS the value onto the current localEulerAngles.\n" +
              "Ex: original (0,0,0) + value (1.8,0,0) → (1.8,0,0).\n" +
              "Tip: combine with Axis Mask to tweak one axis only.",
              "現在の localEulerAngles に角度を【加算】します。\n" +
              "例：元の値 (0,0,0) + 値 (1.8,0,0) → (1.8,0,0)。\n" +
              "ヒント：軸マスクで単一軸のみ変更するのが安全。"));

        DrawHelpEntry(T("SetLocalEuler  —— 旋转（绝对值）", "SetLocalEuler  —— rotation (absolute)", "SetLocalEuler  —— 回転（絶対値）"),
            T("把 localEulerAngles 【直接设为】填写的角度，与原来的旋转无关。",
              "SETS localEulerAngles to the value, ignoring the original rotation.",
              "localEulerAngles を角度に【直接設定】します（元の回転を無視）。"));

        DrawHelpEntry(T("CopyWorldRotation  —— 复制世界旋转", "CopyWorldRotation  —— copy world rotation", "CopyWorldRotation  —— ワールド回転コピー"),
            T("把目标的【世界旋转】对齐到源骨骼的世界旋转（换算回本地空间）。\n" +
              "需要填「源骨骼」名字。",
              "Aligns this bone's WORLD rotation to the source bone (converted back to local space).\n" +
              "Needs a Source Bone.",
              "対象の【ワールド回転】をソースボーンのワールド回転に合わせます（ローカル空間に換算）。\n" +
              "「ソースボーン」名が必要。"));

        EditorGUILayout.Space(6);

        DrawHelpHeader(T("操作类型（缩放）", "Action Types - Scale", "操作タイプ（スケール）"));

        DrawHelpEntry(T("SetLocalScale  —— 缩放（绝对值）", "SetLocalScale  —— scale (absolute)", "SetLocalScale  —— スケール（絶対値）"),
            T("把 localScale 【直接设为】填写的值，无视原缩放。\n" +
              "例：原来 (1, 1, 1)，值 = (0.85, 0.85, 0.85) → 结果是 (0.85, 0.85, 0.85)。",
              "SETS localScale to the value, ignoring the original scale.\n" +
              "Ex: original (1,1,1), value (0.85,0.85,0.85) → (0.85,0.85,0.85).",
              "localScale を値に【直接設定】します（元のスケールを無視）。\n" +
              "例：元の値 (1,1,1)、値 (0.85,0.85,0.85) → (0.85,0.85,0.85)。"));

        DrawHelpEntry(T("MultiplyLocalScale  —— 缩放（相对）", "MultiplyLocalScale  —— scale (multiply)", "MultiplyLocalScale  —— スケール（相対）"),
            T("把 localScale 【乘以】填写的值，在现有缩放基础上做比例微调。\n" +
              "例：原来 (0.8, 0.8, 0.8)，值 = (1, 0.95, 1) → 结果是 (0.8, 0.76, 0.8)。\n" +
              "适合在已有缩放的骨架上做二次微调，常用于只收紧某一根轴。",
              "MULTIPLIES localScale by the value — a relative tweak on top of the current scale.\n" +
              "Ex: original (0.8,0.8,0.8), value (1,0.95,1) → (0.8,0.76,0.8).\n" +
              "Best for a secondary tweak on already-scaled bones.",
              "localScale に値を【乗算】し、現在のスケールに対して比例調整します。\n" +
              "例：元の値 (0.8,0.8,0.8)、値 (1,0.95,1) → (0.8,0.76,0.8)。\n" +
              "既にスケールされたボーンへの二次調整に最適。"));

        DrawHelpEntry(T("CopyWorldScale  —— 复制世界缩放", "CopyWorldScale  —— copy world scale", "CopyWorldScale  —— ワールドスケールコピー"),
            T("把目标的【世界缩放】对齐到源骨骼的世界缩放（按父级 lossyScale 换算回本地缩放）。\n" +
              "需要填「源骨骼」名字。",
              "Aligns this bone's WORLD scale to the source bone (converted via parent lossyScale).\n" +
              "Needs a Source Bone.",
              "対象の【ワールドスケール】をソースボーンのワールドスケールに合わせます（親の lossyScale でローカルに換算）。\n" +
              "「ソースボーン」名が必要。"));
    }

    private void DrawHelpHeader(string text)
    {
        EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
    }

    private void DrawHelpBody(string text)
    {
        EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);
    }

    private void DrawHelpEntry(string title, string desc)
    {
        EditorGUILayout.LabelField("● " + title, EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField("    " + desc, EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(3);
    }

    // ============================== UI ==============================

    private void OnGUI()
    {
        // 包裹整个 GUI，实现「改任何参数都实时刷新预览」
        EditorGUI.BeginChangeCheck();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        // ── 语言切换 ──
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(T("界面语言", "Language", "言語"), GUILayout.Width(70));
        var newLang = (UILanguage)EditorGUILayout.EnumPopup(uiLanguage, GUILayout.Width(120));
        if (newLang != uiLanguage)
        {
            uiLanguage = newLang;
            EditorPrefs.SetInt(PrefsLangKey, (int)uiLanguage);
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        sourceAvatar   = (Transform)EditorGUILayout.ObjectField(
            T("源 Avatar 根节点", "Source Avatar Root", "ソース Avatar ルート"),
            sourceAvatar, typeof(Transform), true);
        targetClothing = (Transform)EditorGUILayout.ObjectField(
            T("目标衣服根节点", "Target Clothing Root", "ターゲット衣装ルート"),
            targetClothing, typeof(Transform), true);

        EditorGUILayout.Space();
        DrawProfileBar();

        if (CurrentProfile != null)
        {
            EditorGUILayout.Space();
            DrawActionButtons();

            EditorGUILayout.Space();
            DrawRuleList();
        }
        else
        {
            EditorGUILayout.HelpBox(
                T("没有预设，点\"新建\"创建一个。",
                  "No profile. Click \"New\" to create one.",
                  "プリセットがありません。「新規」をクリックして作成してください。"),
                MessageType.Warning);
        }

        EditorGUILayout.EndScrollView();

        if (EditorGUI.EndChangeCheck() && isPreviewing)
        {
            RefreshPreview();
        }
    }

    private void DrawProfileBar()
    {
        EditorGUILayout.LabelField(T("预设", "Profiles", "プリセット"), EditorStyles.boldLabel);

        // ── 预设选择 + 新建 / 复制 / 删除 ──
        EditorGUILayout.BeginHorizontal();

        string[] names = new string[profiles.Count];
        for (int i = 0; i < profiles.Count; i++) names[i] = profiles[i].name;

        int newIndex = EditorGUILayout.Popup(currentProfileIndex, names);
        if (newIndex != currentProfileIndex)
        {
            if (isPreviewing) RestorePreview();
            currentProfileIndex = newIndex;
        }

        if (GUILayout.Button(T("新建", "New", "新規"), GUILayout.Width(60)))
        {
            if (isPreviewing) RestorePreview();
            profiles.Add(new Profile(T("新预设 ", "New Profile ", "新プリセット ") + (profiles.Count + 1)));
            currentProfileIndex = profiles.Count - 1;
        }
        if (GUILayout.Button(T("复制", "Duplicate", "複製"), GUILayout.Width(70)))
        {
            if (isPreviewing) RestorePreview();
            if (CurrentProfile != null)
            {
                profiles.Add(CurrentProfile.Duplicate(T(" 副本", " Copy", " のコピー")));
                currentProfileIndex = profiles.Count - 1;
            }
        }
        if (GUILayout.Button(T("删除", "Delete", "削除"), GUILayout.Width(60)))
        {
            if (isPreviewing) RestorePreview();
            if (profiles.Count > 1)
            {
                profiles.RemoveAt(currentProfileIndex);
                currentProfileIndex = Mathf.Clamp(currentProfileIndex, 0, profiles.Count - 1);
            }
            else
            {
                Debug.LogWarning(T("至少要保留一个预设。",
                                   "At least one profile must be kept.",
                                   "少なくとも1つのプリセットを保持してください。"));
            }
        }
        EditorGUILayout.EndHorizontal();

        // ── 保存 / 参考设置 ──
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button(T("保存", "Save", "保存"), GUILayout.Width(80)))
        {
            SaveProfiles();
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button(T("加载参考设置", "Load Reference", "参考設定を読込"), GUILayout.Width(140)))
        {
            if (isPreviewing) RestorePreview();
            profiles.Add(CreateReferenceProfile());
            currentProfileIndex = profiles.Count - 1;
        }

        EditorGUILayout.EndHorizontal();

        if (CurrentProfile != null)
            CurrentProfile.name = EditorGUILayout.TextField(
                T("预设名称", "Profile Name", "プリセット名"), CurrentProfile.name);
    }

    private void DrawActionButtons()
    {
        EditorGUILayout.LabelField(T("操作", "Actions", "操作"), EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(T("自动查找所有骨骼", "Auto-Find All Bones", "全ボーンを自動検索"))) AutoFindAll();
        if (GUILayout.Button(T("全选", "Select All", "全選択")))      SetAllEnabled(true);
        if (GUILayout.Button(T("全不选", "Deselect All", "全解除")))  SetAllEnabled(false);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(isPreviewing))
            if (GUILayout.Button(T("预览", "Preview", "プレビュー"))) StartPreview();
        using (new EditorGUI.DisabledScope(!isPreviewing))
            if (GUILayout.Button(T("还原预览", "Restore Preview", "プレビューを元に戻す"))) RestorePreview();
        if (GUILayout.Button(T("应用到场景 (可 Ctrl+Z 撤销)",
                               "Apply to Scene (Ctrl+Z to Undo)",
                               "シーンに適用 (Ctrl+Z で取消可)")))
            ApplyAllFromUI();
        EditorGUILayout.EndHorizontal();

        if (isPreviewing)
            EditorGUILayout.HelpBox(
                T("预览中：改任何参数都会立刻刷新效果。",
                  "Previewing: parameter changes refresh instantly.",
                  "プレビュー中：パラメータ変更は即座に反映されます。"),
                MessageType.Info);
    }

    private void DrawRuleList()
    {
        EditorGUILayout.LabelField(
            T("规则列表（从上到下依次应用）",
              "Rules (applied top to bottom)",
              "ルール一覧（上から順に適用）"),
            EditorStyles.boldLabel);

        showActionHelp = EditorGUILayout.Foldout(
            showActionHelp,
            T("操作类型 / 轴掩码 说明（点击展开）",
              "Action Type & Axis Mask Help (click to expand)",
              "操作タイプ / 軸マスク 説明（クリックで展開）"),
            true);

        if (showActionHelp)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawActionHelpContent();
            EditorGUILayout.EndVertical();
        }

        int moveFrom = -1, moveTo = -1, deleteIndex = -1;

        for (int i = 0; i < CurrentProfile.rules.Count; i++)
        {
            var rule = CurrentProfile.rules[i];

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // ── Row 1 ──
            EditorGUILayout.BeginHorizontal();

            rule.enabled = EditorGUILayout.Toggle(rule.enabled, GUILayout.Width(20));

            using (new EditorGUI.DisabledScope(!rule.enabled))
            {
                rule.boneName = EditorGUILayout.TextField(rule.boneName, GUILayout.Width(110));
                rule.action   = (ActionType)EditorGUILayout.EnumPopup(rule.action, GUILayout.Width(150));

                GUILayout.Label(
                    new GUIContent("?", GetActionTooltip(rule.action)),
                    EditorStyles.miniLabel,
                    GUILayout.Width(16));

                if (rule.NeedsSource())
                {
                    EditorGUILayout.LabelField(
                        T("（不用填值）", "(no value needed)", "（値不要）"),
                        GUILayout.Width(180));
                }
                else
                {
                    rule.value = EditorGUILayout.Vector3Field("", rule.value, GUILayout.Width(180));
                }
            }

            if (GUILayout.Button("↑", GUILayout.Width(24)) && i > 0)
            { moveFrom = i; moveTo = i - 1; }
            if (GUILayout.Button("↓", GUILayout.Width(24)) && i < CurrentProfile.rules.Count - 1)
            { moveFrom = i; moveTo = i + 1; }
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            { deleteIndex = i; }

            EditorGUILayout.EndHorizontal();

            // ── Row 2 ──
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!rule.enabled))
            {
                EditorGUILayout.LabelField(T("轴掩码", "Axis Mask", "軸マスク"), GUILayout.Width(60));

                rule.axisMask.x = EditorGUILayout.Toggle(rule.axisMask.x > 0.5f, GUILayout.Width(16)) ? 1f : 0f;
                EditorGUILayout.LabelField("X", GUILayout.Width(14));
                rule.axisMask.y = EditorGUILayout.Toggle(rule.axisMask.y > 0.5f, GUILayout.Width(16)) ? 1f : 0f;
                EditorGUILayout.LabelField("Y", GUILayout.Width(14));
                rule.axisMask.z = EditorGUILayout.Toggle(rule.axisMask.z > 0.5f, GUILayout.Width(16)) ? 1f : 0f;
                EditorGUILayout.LabelField("Z", GUILayout.Width(14));

                GUILayout.Space(10);

                if (rule.NeedsSource())
                {
                    EditorGUILayout.LabelField(T("源骨骼", "Source Bone", "ソースボーン"), GUILayout.Width(70));
                    rule.sourceBoneName = EditorGUILayout.TextField(rule.sourceBoneName, GUILayout.Width(80));
                    GUILayout.Space(10);
                }

                EditorGUILayout.LabelField(T("目标", "Target", "ターゲット"), GUILayout.Width(45));
                rule.target = (Transform)EditorGUILayout.ObjectField(rule.target, typeof(Transform), true);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        if (moveFrom >= 0 && moveTo >= 0)
        {
            var r = CurrentProfile.rules[moveFrom];
            CurrentProfile.rules.RemoveAt(moveFrom);
            CurrentProfile.rules.Insert(moveTo, r);
        }
        if (deleteIndex >= 0)
        {
            CurrentProfile.rules.RemoveAt(deleteIndex);
        }

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(T("＋ 添加规则", "+ Add Rule", "＋ ルール追加")))
        {
            CurrentProfile.rules.Add(new Rule("", ActionType.SetLocalScale, Vector3.one));
        }
        if (GUILayout.Button(T("＋ 快捷：复制世界位置", "+ Quick: Copy World Position", "＋ クイック：ワールド位置コピー")))
        {
            CurrentProfile.rules.Add(new Rule("", ActionType.CopyWorldPosition, Vector3.zero, null, ""));
        }
        if (GUILayout.Button(T("＋ 快捷：复制世界旋转", "+ Quick: Copy World Rotation", "＋ クイック：ワールド回転コピー")))
        {
            CurrentProfile.rules.Add(new Rule("", ActionType.CopyWorldRotation, Vector3.zero, null, ""));
        }
        EditorGUILayout.EndHorizontal();
    }

    // ============================== 逻辑：预览 ==============================

    private void SetAllEnabled(bool value)
    {
        if (CurrentProfile == null) return;
        foreach (var r in CurrentProfile.rules) r.enabled = value;
        if (isPreviewing) RefreshPreview();
    }

    private void StartPreview()
    {
        if (CurrentProfile == null) return;

        snapshot.Clear();
        CaptureSnapshot();
        isPreviewing = true;
        ApplyAllInternal(false);
    }

    private void RefreshPreview()
    {
        if (!isPreviewing) return;
        CaptureSnapshot();
        RestoreSnapshotOnly();
        ApplyAllInternal(false);
    }

    private void RestorePreview()
    {
        if (!isPreviewing) return;
        RestoreSnapshotOnly();
        isPreviewing = false;
        snapshot.Clear();
    }

    private void CaptureSnapshot()
    {
        if (CurrentProfile == null) return;
        foreach (var rule in CurrentProfile.rules)
        {
            if (rule.target == null) continue;
            if (snapshot.ContainsKey(rule.target)) continue;
            snapshot[rule.target] = new TransformState
            {
                localPosition = rule.target.localPosition,
                localRotation = rule.target.localRotation,
                localScale    = rule.target.localScale
            };
        }
    }

    private void RestoreSnapshotOnly()
    {
        foreach (var kv in snapshot)
        {
            if (kv.Key == null) continue;
            kv.Key.localPosition = kv.Value.localPosition;
            kv.Key.localRotation = kv.Value.localRotation;
            kv.Key.localScale    = kv.Value.localScale;
        }
    }

    private void ApplyAllFromUI()
    {
        if (CurrentProfile == null) return;

        if (isPreviewing)
        {
            RestoreSnapshotOnly();
            isPreviewing = false;
            snapshot.Clear();
        }

        foreach (var rule in CurrentProfile.rules)
        {
            if (!rule.enabled || rule.target == null) continue;
            Undo.RecordObject(rule.target, "Bone Adjustment");
        }

        ApplyAllInternal(true);
        Debug.Log(T("骨骼适配规则已全部应用。",
                    "All bone adjustment rules applied.",
                    "すべてのボーン調整ルールを適用しました。"));
    }

    private void ApplyAllInternal(bool verbose)
    {
        if (CurrentProfile == null) return;
        foreach (var rule in CurrentProfile.rules)
        {
            if (!rule.enabled) continue;
            if (rule.target == null)
            {
                if (verbose)
                    Debug.LogWarning(T(
                        $"规则 [{rule.boneName}] 没有指定目标 Transform，已跳过。",
                        $"Rule [{rule.boneName}] has no Target Transform, skipped.",
                        $"ルール [{rule.boneName}] にターゲット Transform が指定されていません。スキップしました。"));
                continue;
            }
            ApplyRule(rule);
        }
    }

    private void AutoFindAll()
    {
        if (targetClothing == null)
        {
            Debug.LogWarning(T("请先指定目标衣服根节点。",
                               "Please specify the Target Clothing Root first.",
                               "先にターゲット衣装ルートを指定してください。"));
            return;
        }
        if (CurrentProfile == null) return;

        foreach (var rule in CurrentProfile.rules)
        {
            rule.target = FindChildByName(targetClothing, rule.boneName);
            if (!string.IsNullOrEmpty(rule.sourceBoneName) && sourceAvatar != null)
                rule.source = FindChildByName(sourceAvatar, rule.sourceBoneName);
        }

        if (isPreviewing) RefreshPreview();
    }

    private Transform FindChildByName(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var result = FindChildByName(child, name);
            if (result != null) return result;
        }
        return null;
    }

    // ============================== 应用单条规则 ==============================

    private void ApplyRule(Rule rule)
    {
        var t = rule.target;
        Vector3 mask = new Vector3(
            rule.axisMask.x > 0.5f ? 1f : 0f,
            rule.axisMask.y > 0.5f ? 1f : 0f,
            rule.axisMask.z > 0.5f ? 1f : 0f);

        switch (rule.action)
        {
            case ActionType.MoveLocal:
            {
                Vector3 desired = t.localPosition + rule.value;
                t.localPosition = Blend(t.localPosition, desired, mask);
                break;
            }
            case ActionType.SetLocalPosition:
            {
                t.localPosition = Blend(t.localPosition, rule.value, mask);
                break;
            }
            case ActionType.CopyWorldPosition:
            {
                if (rule.source == null)
                {
                    Debug.LogWarning(T(
                        $"CopyWorldPosition 规则 [{rule.boneName}] 没有找到源 Transform，已跳过。",
                        $"CopyWorldPosition rule [{rule.boneName}] has no Source Transform, skipped.",
                        $"CopyWorldPosition ルール [{rule.boneName}] にソース Transform が見つかりません。スキップしました。"));
                    break;
                }
                Vector3 desired = t.parent != null
                    ? t.parent.InverseTransformPoint(rule.source.position)
                    : rule.source.position;
                t.localPosition = Blend(t.localPosition, desired, mask);
                break;
            }
            case ActionType.AddLocalEuler:
            {
                Vector3 desired = t.localEulerAngles + rule.value;
                t.localEulerAngles = BlendEuler(t.localEulerAngles, desired, mask);
                break;
            }
            case ActionType.SetLocalEuler:
            {
                t.localEulerAngles = BlendEuler(t.localEulerAngles, rule.value, mask);
                break;
            }
            case ActionType.CopyWorldRotation:
            {
                if (rule.source == null)
                {
                    Debug.LogWarning(T(
                        $"CopyWorldRotation 规则 [{rule.boneName}] 没有找到源 Transform，已跳过。",
                        $"CopyWorldRotation rule [{rule.boneName}] has no Source Transform, skipped.",
                        $"CopyWorldRotation ルール [{rule.boneName}] にソース Transform が見つかりません。スキップしました。"));
                    break;
                }
                Quaternion desiredRot = t.parent != null
                    ? Quaternion.Inverse(t.parent.rotation) * rule.source.rotation
                    : rule.source.rotation;
                t.localEulerAngles = BlendEuler(t.localEulerAngles, desiredRot.eulerAngles, mask);
                break;
            }
            case ActionType.SetLocalScale:
            {
                t.localScale = Blend(t.localScale, rule.value, mask);
                break;
            }
            case ActionType.MultiplyLocalScale:
            {
                Vector3 desired = Vector3.Scale(t.localScale, rule.value);
                t.localScale = Blend(t.localScale, desired, mask);
                break;
            }
            case ActionType.CopyWorldScale:
            {
                if (rule.source == null)
                {
                    Debug.LogWarning(T(
                        $"CopyWorldScale 规则 [{rule.boneName}] 没有找到源 Transform，已跳过。",
                        $"CopyWorldScale rule [{rule.boneName}] has no Source Transform, skipped.",
                        $"CopyWorldScale ルール [{rule.boneName}] にソース Transform が見つかりません。スキップしました。"));
                    break;
                }
                Vector3 parentScale = t.parent != null ? t.parent.lossyScale : Vector3.one;
                Vector3 desired = new Vector3(
                    parentScale.x != 0 ? rule.source.lossyScale.x / parentScale.x : rule.source.lossyScale.x,
                    parentScale.y != 0 ? rule.source.lossyScale.y / parentScale.y : rule.source.lossyScale.y,
                    parentScale.z != 0 ? rule.source.lossyScale.z / parentScale.z : rule.source.lossyScale.z);
                t.localScale = Blend(t.localScale, desired, mask);
                break;
            }
        }
    }

    private static Vector3 Blend(Vector3 oldV, Vector3 newV, Vector3 mask)
    {
        return new Vector3(
            mask.x > 0.5f ? newV.x : oldV.x,
            mask.y > 0.5f ? newV.y : oldV.y,
            mask.z > 0.5f ? newV.z : oldV.z);
    }

    private static Vector3 BlendEuler(Vector3 oldE, Vector3 newE, Vector3 mask)
    {
        return new Vector3(
            mask.x > 0.5f ? newE.x : oldE.x,
            mask.y > 0.5f ? newE.y : oldE.y,
            mask.z > 0.5f ? newE.z : oldE.z);
    }
}