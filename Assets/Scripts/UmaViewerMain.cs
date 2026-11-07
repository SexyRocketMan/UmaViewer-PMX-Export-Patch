using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using System;
using Newtonsoft.Json.Linq;
using System.Collections;
using UnityEngine.Rendering;

public class UmaViewerMain : MonoBehaviour
{
    public static UmaViewerMain Instance;
    private UmaViewerUI UI => UmaViewerUI.Instance;
    private UmaViewerBuilder Builder => UmaViewerBuilder.Instance;

    public RenderPipelineAsset DefaultRenderPipeline;
    public List<CharaEntry> Characters = new List<CharaEntry>();
    public List<CharaEntry> MobCharacters = new List<CharaEntry>();
    public List<LiveEntry> Lives = new List<LiveEntry>();
    public List<CostumeEntry> Costumes = new List<CostumeEntry>();
    public Dictionary<string,UmaDatabaseEntry> AbList = new Dictionary<string, UmaDatabaseEntry>();
    public List<UmaDatabaseEntry> AbMotions = new List<UmaDatabaseEntry>();
    public List<UmaDatabaseEntry> AbSounds = new List<UmaDatabaseEntry>();
    public List<UmaDatabaseEntry> AbChara = new List<UmaDatabaseEntry>();
    public List<UmaDatabaseEntry> AbEffect = new List<UmaDatabaseEntry>();
    public List<UmaDatabaseEntry> CostumeList = new List<UmaDatabaseEntry>();

    public Dictionary<TranslationTables, Dictionary<int, string>> Translations = new Dictionary<TranslationTables, Dictionary<int, string>>() 
    { 
        {TranslationTables.UmaNames, new Dictionary<int,string>() },
        //{TranslationTables.Costumes, new Dictionary<int,string>() }, //unused (can't correlate costume model IDs with names without more info from the database)
        {TranslationTables.MobNames, new Dictionary<int,string>() },
    };

    private void Awake()
    {
        Instance = this;
        Config.Ensure();
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 120;

        AbList = UmaDatabaseController.Instance.MetaEntries;
        if (AbList == null) return;
        var chara_3d = AbList.Where(ab => ab.Value.Type == UmaFileType._3d_cutt).Select(ab => ab.Value).ToList();
        AbChara = chara_3d.FindAll(ab => ab.Name.StartsWith(UmaDatabaseController.CharaPath));
        AbMotions = chara_3d.FindAll(ab => ab.Name.StartsWith(UmaDatabaseController.MotionPath));
        AbEffect = chara_3d.FindAll(ab => ab.Name.StartsWith(UmaDatabaseController.EffectPath));
        AbSounds = AbList.Where(ab => ab.Value.Type == UmaFileType.sound).Select(ab => ab.Value).ToList();
        var outgame = AbList.Where(ab => ab.Value.Type == UmaFileType.outgame).Select(ab => ab.Value).ToList();
        CostumeList = outgame.FindAll(e => e.Name.StartsWith(UmaDatabaseController.CostumePath));
    }

    private IEnumerator Start()
    {
        if (AbList == null) yield break;
        int loadingStep = 0;
        int loadingStepsTotal = 10;
        var UmaCharaData = UmaDatabaseController.Instance.CharaData;
        var MobCharaData = UmaDatabaseController.Instance.MobCharaData;
        var loadingUI = UmaSceneController.instance;


        //修改(载入通用服装ColorSet相关)
        var charaDressColor = UmaDatabaseController.Instance.CharaDressColor;



        //EN Translations
        loadingUI.LoadingProgressChange(loadingStep++, loadingStepsTotal, "Downloading Translations");
        if (Config.Instance.Language == Language.En)
        {
            var translationsUrl = "https://raw.githubusercontent.com/UmaTL/hachimi-tl-en/refs/heads/main/localized_data/text_data_dict.json";
            yield return UmaViewerDownload.DownloadText(translationsUrl, txt =>
            {
                // A download that worked is kept beside Config.json, so the next start - or a machine without a
                // working network - still has the names. Without that, a headless run silently exported the
                // japanese name in the english name field whenever GitHub was unreachable, which is the kind of
                // difference nobody notices until they compare two files.
                if (string.IsNullOrEmpty(txt))
                {
                    LoadCachedTranslations();
                    return;
                }
                try
                {
                    ApplyTranslations(JObject.Parse(txt));
                    SaveCachedTranslations(txt);
                }
                catch
                {
                    UI.ShowMessage("Loading Translations failed", UIMessageType.Error);
                    LoadCachedTranslations();
                }
            });
        }

        if (Config.Instance.WorkMode == WorkMode.Standalone)
        {
            var umaIcons = UmaCharaData.Select(item => AbList.TryGetValue($"chara/chr{item["id"]}/chr_icon_{item["id"]}", out UmaDatabaseEntry entry) ? entry : null).Where(entry => entry != null);
            var mobIcons = MobCharaData.Select(item => AbList.TryGetValue($"mob/mob_chr_icon_{item["mob_id"]}_000001_01", out UmaDatabaseEntry entry) ? entry : null).Where(entry => entry != null);
            var costumeIcons = CostumeList;
            List<UmaDatabaseEntry> filesToDownload = umaIcons.Concat(mobIcons).Concat(costumeIcons).ToList();
            yield return UmaViewerDownload.DownloadAssets(filesToDownload, UmaSceneController.instance.LoadingProgressChange);
            filesToDownload.Clear();
        }

        // Force Global-Default mode on android for now
        // Upd: This didn't work at all
        #if UNITY_ANDROID
            Config.Instance.WorkMode = WorkMode.Default;
            Config.Instance.Region = Region.Global;
        #endif

        loadingUI.LoadingProgressChange(loadingStep++, loadingStepsTotal, "Loading Character Data");
        yield return null;
        foreach (var item in UmaCharaData)
        {
            var id = Convert.ToInt32(item["id"]);
            if (!Characters.Where(c => c.Id == id).Any())
            {
                Characters.Add(new CharaEntry()
                {
                    Name = item["charaname"].ToString(),
                    EnName = Translations[TranslationTables.UmaNames].ContainsKey(id) ? Translations[TranslationTables.UmaNames][id] : "",
                    Icon = UmaViewerBuilder.Instance.LoadCharaIcon(id.ToString()),
                    Id = id,
                    ThemeColor = "#" + item["ui_nameplate_color_1"].ToString()
                });
            }
        }

        loadingUI.LoadingProgressChange(loadingStep++, loadingStepsTotal, "Loading Mob Data");
        yield return null;
        foreach (var item in MobCharaData)
        {
            if (Convert.ToInt32(item["use_live"]) == 0) 
            {
                continue;
            }

            var id = Convert.ToInt32(item["mob_id"]);
            MobCharacters.Add(new CharaEntry()
            {
                Name = item["charaname"].ToString(),
                EnName = Translations[TranslationTables.MobNames].ContainsKey(id) ? Translations[TranslationTables.MobNames][id] : "",
                Icon = UmaViewerBuilder.Instance.LoadMobCharaIcon(id.ToString()),
                Id = id,
                IsMob = true
            });
        }

        loadingUI.LoadingProgressChange(loadingStep++, loadingStepsTotal, "Loading Costumes Data");
        yield return null;
        foreach (var item in CostumeList)
        {
            var costume = new CostumeEntry();
            var name = Path.GetFileName(item.Name);
            costume.Id = name.Replace("dress_","");
            costume.Icon = Builder.LoadSprite(item);
            Costumes.Add(costume);
        }

        var DressData = UmaDatabaseController.Instance.DressData;
        foreach (var data in DressData)
        {
            var costume = Costumes.FirstOrDefault(a => a.Id.Split('_')[0].Contains(data["id"].ToString()) );
            if (costume != null)
            {
                costume.CharaId = Convert.ToInt32(data["chara_id"]);
                costume.DressName = data["dressname"].ToString();
                costume.BodyType = Convert.ToInt32(data["body_type"]);
                costume.BodyTypeSub = Convert.ToInt32(data["body_type_sub"]);
            }
        }

        loadingUI.LoadingProgressChange(loadingStep++, loadingStepsTotal, "Loading Live Data");
        yield return null;
        var asset = AbList["livesettings"];
        if (asset != null)
        {
            string filePath = asset.FilePath;
            if (File.Exists(filePath))
            {
                AssetBundle bundle = UmaAssetManager.LoadAssetBundle(asset, true);
                foreach (var item in UmaDatabaseController.Instance.LiveData)
                {
                    var musicId = Convert.ToInt32(item["music_id"]);
                    var songName = item["songname"].ToString();
                    var membercount = Convert.ToInt32(item["live_member_number"]);
                    var defaultdress = Convert.ToInt32(item["default_main_dress"]);

                    if (!Lives.Where(c => c.MusicId == musicId).Any())
                    {
                        if (bundle.Contains(musicId.ToString()))
                        {
                            TextAsset liveData = bundle.LoadAsset<TextAsset>(musicId.ToString());

                            Lives.Add(new LiveEntry(liveData.text)
                            {
                                MusicId = musicId,
                                SongName = songName,
                                MemberCount = membercount,
                                DefaultDress = defaultdress,
                                Icon = UmaViewerBuilder.Instance.LoadLiveIcon(musicId)
                            }); 
                        }
                    }
                }
            }
        }

        loadingUI.LoadingProgressChange(loadingStep++, loadingStepsTotal, "Loading UI");
        yield return null;
        UI.LoadModelPanels();
        loadingUI.LoadingProgressChange(loadingStep++, loadingStepsTotal, "Loading UI");
        yield return null;
        UI.LoadMiniModelPanels();
        loadingUI.LoadingProgressChange(loadingStep++, loadingStepsTotal, "Loading UI");
        yield return null;
        UI.LoadPropPanel();
        loadingUI.LoadingProgressChange(loadingStep++, loadingStepsTotal, "Loading UI");
        yield return null;
        UI.LoadMapPanel();
        loadingUI.LoadingProgressChange(loadingStep++, loadingStepsTotal, "Loading UI");
        yield return null;
        UI.LoadLivePanels();
        loadingUI.LoadingProgressChange(-1, -1);

        //Load Shader First
        var shaders = UmaAssetManager.LoadAssetBundle(AbList["shader"], true);
        Builder.ShaderList = new List<Shader>(shaders.LoadAllAssets<Shader>()); 
    }

    /// <summary>Where the english translations are remembered between runs: beside the install's Config.json.</summary>
    private static string TranslationCachePath()
    {
        // the folder the build or the project lives in, which is where Config.json is - not persistentDataPath,
        // which a portable copy of the viewer would not carry with it
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(folder, "translations_en.json");
    }

    /// <summary>Reads the cached tables back when the download did not happen. Silent when there is no cache.</summary>
    private void LoadCachedTranslations()
    {
        try
        {
            string path = TranslationCachePath();
            if (!File.Exists(path)) return;
            ApplyTranslations(JObject.Parse(File.ReadAllText(path)));
            Debug.Log($"[UmaViewer] english translations loaded from {path}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UmaViewer] could not read the cached translations: {e.Message}");
        }
    }

    /// <summary>Keeps the downloaded tables for the next run.</summary>
    private void SaveCachedTranslations(string json)
    {
        try
        {
            File.WriteAllText(TranslationCachePath(), json);
            Debug.Log($"[UmaViewer] english translations cached to {TranslationCachePath()}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UmaViewer] could not cache the translations: {e.Message}");
        }
    }

    /// <summary>Fills the translation tables from a parsed `text_data_dict.json`.</summary>
    private void ApplyTranslations(JObject translations)
    {
        Translations[TranslationTables.UmaNames] =
            translations[((int)TranslationTables.UmaNames).ToString()].ToObject<Dictionary<int, string>>();
        Translations[TranslationTables.MobNames] =
            translations[((int)TranslationTables.MobNames).ToString()].ToObject<Dictionary<int, string>>();
    }

    public void OpenUrl(string url)
    {
        Application.OpenURL(url);
    }

}
