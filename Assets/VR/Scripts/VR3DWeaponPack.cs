// DFU Quest3 VR — 3D weapon/shield pack loader for westingtyler's "3D Weapons
// Shields And Items Array" model pack.
//
// WHY NOT THE ORIGINAL ASSETBUNDLE: the pack ships as a StandaloneWindows-targeted
// UnityFS bundle (2019.4.40f1, DXT textures). An Android player cannot load a
// Windows bundle — platform + texture-format tags are baked in (verified on device:
// AssetBundle.LoadFromFile returns null with zero log output). So we PRE-EXTRACT on
// PC (UnityPy) into a platform-neutral format:
//
//   3d-weapons-pack.bin  ("W3DP" v1): [i32 count] then per entry:
//       i32 id, i32 vcount, i32 icount, i32 indexSize(2|4),
//       vcount * (3f pos + 3f normal + 2f uv),  icount * (u16|u32)
//   3d-weapons-atlas.png  the pack's single 1024x1024 texture atlas.
// Both are pushed to the app files dir (adb push) and loaded with plain .NET APIs.
//
// Asset id scheme (from the mod's ID spreadsheet; verified against the bundle's own
// manifest — 448 prefabs, all 220 ids we request are present):
//   Weapons: 128000 + matDigit*100 + weaponIdx   e.g. 128005 = iron longsword
//   Shields: 132000 + matDigit*100 + (70+typeIdx) e.g. 132071 = iron round shield
//     weaponIdx = DFU classic Weapons enum - 112 (Dagger=113 -> 1 ... Long_Bow=130 -> 18)
//     matDigit: 0 Iron, 1 Steel, 2 Silver, 3 Elven, 4 Dwarven, 5 Mithril,
//               6 Adamantium, 7 Ebony, 8 Orcish, 9 Daedric  (MetalTypes value - 1)
//     shield typeIdx: 0 Buckler, 1 Round, 2 Kite, 3 Tower — v0.906 models ONLY these;
//                     bigger shields fold onto Tower (ShieldPrefabId clamps 4-9 -> 3)
//
// Auto-sizing: each mesh is scaled so its longest axis matches a classic-reasonable
// length for the weapon class (logged on attach for on-device tuning).
using System.Collections.Generic;
using UnityEngine;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Items;

namespace DFUQuest3
{
    public class VR3DWeaponPack : MonoBehaviour
    {
        public static VR3DWeaponPack Instance { get; private set; }

        public string packFileName = "3d-weapons-pack.bin";
        public string atlasFileName = "3d-weapons-atlas.png";
        public bool enabled3D = true;

        class PackMesh
        {
            public Vector3[] verts;
            public Vector3[] normals;
            public Vector2[] uvs;
            public int[] indices;
        }

        readonly Dictionary<int, PackMesh> meshes = new Dictionary<int, PackMesh>();
        Texture2D atlas;
        bool surveyed;

        // Classic-reasonable longest-axis length (meters) per classic weapon type.
        public float TargetLength(Weapons t)
        {
            switch (t)
            {
                case Weapons.Dagger: case Weapons.Tanto: return 0.30f;
                case Weapons.Shortsword: case Weapons.Wakazashi: return 0.55f;
                case Weapons.Longsword: return 0.75f;
                case Weapons.Broadsword: return 0.85f;
                case Weapons.Claymore: case Weapons.Dai_Katana: return 1.15f;
                case Weapons.Saber: return 0.80f;
                case Weapons.Katana: return 0.85f;
                case Weapons.Mace: return 0.55f;
                case Weapons.Staff: return 1.55f;
                case Weapons.Flail: return 0.65f;
                case Weapons.Warhammer: return 0.80f;
                case Weapons.Battle_Axe: return 0.85f;
                case Weapons.War_Axe: return 0.65f;
                default: return 0.70f;
            }
        }

        // Classic Weapons enum (Dagger=113..Long_Bow=130) -> the pack's weapon index.
        // EXPLICIT TABLE — dfu's enum order and the pack's count order DIVERGE in the
        // middle (Tanto..Mace): an arithmetic (-112) map put Staff on the shortsword
        // mesh etc. (device-verified 2026-10-03: staff equip attached 128203, a silver
        // shortsword). Pack indices verified against real bundle prefab names.
        // DFU: Dagger113 Tanto114 Staff115 Shortsword116 Wakazashi117 Broadsword118
        //      Saber119 Longsword120 Katana121 Claymore122 DaiKatana123 Mace124
        //      Flail125 Warhammer126 BattleAxe127 WarAxe128 ShortBow129 LongBow130
        // Pack: 1 Dagger, 2 Tanto, 3 Shortsword, 4 Wakizashi, 5 Longsword, 6 Broadsword,
        //       7 Claymore, 8 Saber, 9 Katana, 10 DaiKatana, 11 Mace, 12 Staff,
        //       13 Flail, 14 Warhammer, 15 BattleAxe, 16 WarAxe, 17 ShortBow, 18 LongBow
        public static int WeaponIndex(Weapons t)
        {
            switch (t)
            {
                case Weapons.Dagger: return 1;
                case Weapons.Tanto: return 2;
                case Weapons.Shortsword: return 3;
                case Weapons.Wakazashi: return 4;
                case Weapons.Longsword: return 5;
                case Weapons.Broadsword: return 6;
                case Weapons.Claymore: return 7;
                case Weapons.Saber: return 8;
                case Weapons.Katana: return 9;
                case Weapons.Dai_Katana: return 10;
                case Weapons.Mace: return 11;
                case Weapons.Staff: return 12;
                case Weapons.Flail: return 13;
                case Weapons.Warhammer: return 14;
                case Weapons.Battle_Axe: return 15;
                case Weapons.War_Axe: return 16;
                case Weapons.Short_Bow: return 17;
                case Weapons.Long_Bow: return 18;
                default: return 0;
            }
        }

        // MetalTypes value - 1 == the mod's material digit (None/unknown -> Iron).
        public static int MaterialDigit(MetalTypes m)
        {
            int v = (int)m - 1;
            if (v < 0 || v > 9) v = 0;
            return v;
        }

        public static int ShieldPrefabId(MetalTypes m, int shieldTypeIdx)
        {
            int t = shieldTypeIdx < 0 ? 0 : (shieldTypeIdx > 3 ? 3 : shieldTypeIdx);
            return 132000 + MaterialDigit(m) * 100 + 70 + t;
        }

        public static int WeaponPrefabId(MetalTypes m, Weapons t)
        {
            return 128000 + MaterialDigit(m) * 100 + WeaponIndex(t);
        }

        static readonly (string contains, int idx)[] ShieldTypes =
        {
            ("buckler", 0), ("round", 1), ("kite", 2), ("tower", 3), ("heater", 4),
            ("warden", 5), ("wardoor", 6), ("apex", 7), ("nguni", 8), ("rupe", 9),
        };
        public static int ShieldTypeIndex(string shieldName)
        {
            if (string.IsNullOrEmpty(shieldName)) return 0;
            string n = shieldName.ToLowerInvariant();
            foreach (var row in ShieldTypes)
                if (n.Contains(row.contains)) return row.idx;
            return 0;
        }

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            LoadPack();
        }

        // Pack files live in persistentDataPath (= /sdcard/Android/data/<pkg>/files,
        // where adb push delivers them). Plain File.ReadAllBytes — no mmap/FUSE quirks,
        // no platform-gated AssetBundle; all parsing is our own little-endian reader.
        string PackPath(string fileName)
        {
            return System.IO.Path.Combine(Application.persistentDataPath, fileName);
        }

        void LoadPack()
        {
            if (!enabled3D) return;
            string packPath = PackPath(packFileName);
            string atlasPath = PackPath(atlasFileName);
            if (!System.IO.File.Exists(packPath) || !System.IO.File.Exists(atlasPath))
            {
                Debug.Log("[DFUQuest3] 3D pack files not present at " + packPath +
                    " (adb push 3d-weapons-pack.bin + 3d-weapons-atlas.png). Sprite fallback stays.");
                return;
            }

            try
            {
                atlas = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                atlas.LoadImage(System.IO.File.ReadAllBytes(atlasPath));
                atlas.wrapMode = TextureWrapMode.Clamp;
                atlas.filterMode = FilterMode.Bilinear;
                atlas.name = "VR3DWeaponsAtlas";

                byte[] data = System.IO.File.ReadAllBytes(packPath);
                LoadPackData(data);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[DFUQuest3] 3D pack load exception: " + e.Message);
                meshes.Clear();
                atlas = null;
            }
        }

        void LoadPackData(byte[] data)
        {
            int idx = 8; // skip 'W3DP' magic(4) + version(4)
            if (data.Length < 8 || data[0] != 'W' || data[1] != '3' || data[2] != 'D' || data[3] != 'P')
                throw new System.Exception("bad magic");
            int count = System.BitConverter.ToInt32(data, idx); idx += 4;

            for (int e = 0; e < count; e++)
            {
                int id = System.BitConverter.ToInt32(data, idx); idx += 4;
                int vcount = System.BitConverter.ToInt32(data, idx); idx += 4;
                int icount = System.BitConverter.ToInt32(data, idx); idx += 4;
                int indexSize = System.BitConverter.ToInt32(data, idx); idx += 4;

                var pm = new PackMesh();
                pm.verts = new Vector3[vcount];
                pm.normals = new Vector3[vcount];
                pm.uvs = new Vector2[vcount];
                // LAYOUT mirrors the writer's SoA blocks: ALL positions, then ALL uvs,
                // then ALL normals (each vertex after the first shifts otherwise —
                // first-generation device test showed scrambled UVs + stretched faces).
                for (int v = 0; v < vcount; v++)
                {
                    pm.verts[v] = new Vector3(
                        System.BitConverter.ToSingle(data, idx),
                        System.BitConverter.ToSingle(data, idx + 4),
                        System.BitConverter.ToSingle(data, idx + 8)); idx += 12;
                }
                for (int v = 0; v < vcount; v++)
                {
                    pm.uvs[v] = new Vector2(
                        System.BitConverter.ToSingle(data, idx),
                        System.BitConverter.ToSingle(data, idx + 4)); idx += 8;
                }
                for (int v = 0; v < vcount; v++)
                {
                    pm.normals[v] = new Vector3(
                        System.BitConverter.ToSingle(data, idx),
                        System.BitConverter.ToSingle(data, idx + 4),
                        System.BitConverter.ToSingle(data, idx + 8)); idx += 12;
                }
                pm.indices = new int[icount];
                if (indexSize == 2)
                {
                    for (int i = 0; i < icount; i++)
                    {
                        pm.indices[i] = System.BitConverter.ToUInt16(data, idx); idx += 2;
                    }
                }
                else
                {
                    for (int i = 0; i < icount; i++)
                    {
                        pm.indices[i] = (int)System.BitConverter.ToUInt32(data, idx); idx += 4;
                    }
                }
                if (!meshes.ContainsKey(id)) meshes[id] = pm;
            }

            Debug.Log("[DFUQuest3] 3D pack loaded: " + meshes.Count + " models, atlas " +
                (atlas != null ? atlas.width + "x" + atlas.height : "MISSING"));
        }

        public bool PackLoaded { get { return meshes.Count > 0 && atlas != null; } }

        public bool HasModel(int id) { return meshes.ContainsKey(id); }

        public void SurveyIfReady()
        {
            if (surveyed || !PackLoaded) return;
            surveyed = true;
            // Digest-level survey: our 4 material families x (18 weapons + 4 shields).
            int weaponsPresent = 0, shieldsPresent = 0, sampleRows = 0;
            var missingSample = new List<string>();
            for (int mat = 0; mat < 10; mat++)
            {
                int wRow = 0, sRow = 0;
                for (int wid = 1; wid <= 18; wid++)
                    if (meshes.ContainsKey(128000 + mat * 100 + wid)) { wRow++; weaponsPresent++; }
                for (int s = 0; s < 4; s++)
                    if (meshes.ContainsKey(132000 + mat * 100 + 70 + s)) { sRow++; shieldsPresent++; }
                if (sampleRows < 3)
                {
                    Debug.Log("[DFUQuest3] 3DPACK survey row mat=" + mat +
                        " weapons=" + wRow + "/18" + " shields=" + sRow + "/4");
                    sampleRows++;
                }
            }
            foreach (var kv in meshes)
            {
                if (missingSample.Count >= 4) break;
                if (kv.Key < 128000 || kv.Key > 132999) missingSample.Add(kv.Key.ToString());
            }
            Debug.Log("[DFUQuest3] 3DPACK SURVEY: weapons=" + weaponsPresent + "/180" +
                " shields=" + shieldsPresent + "/40" + " total=" + meshes.Count +
                (missingSample.Count > 0 ? " nonstandard-ids=" + string.Join(",", missingSample.ToArray()) : ""));
        }

        /// <summary>
        /// Build a hand model GameObject for a numeric pack id (fresh GO each call;
        /// consumer scales/positions it and destroys it on change). Null when absent.
        /// </summary>
        public GameObject CreateHandModel(int id, string goName)
        {
            PackMesh pm;
            if (!meshes.TryGetValue(id, out pm))
            {
                SurveyIfReady();
                Debug.Log("[DFUQuest3] 3D pack MISS id=" + id + " (sprite fallback)");
                return null;
            }

            var mesh = new Mesh();
            mesh.vertices = pm.verts;
            mesh.normals = pm.normals;
            mesh.uv = pm.uvs;
            mesh.triangles = pm.indices;
            mesh.RecalculateBounds();

            var mat = new Material(Shader.Find("Unlit/Texture"));
            mat.mainTexture = atlas;

            var go = new GameObject(goName);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            return go;
        }
    }
}