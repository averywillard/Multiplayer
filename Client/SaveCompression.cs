﻿using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public static class SaveCompression
    {
        public static bool doSaveCompression;
        private static Dictionary<ushort, ThingDef> thingDefsByShortHash;

        public static void Save(Map map)
        {
            if (Scribe.mode != LoadSaveMode.Saving) return;

            map.compressor.compressibilityDecider = new CompressibilityDecider(map);

            BinaryWriter rockData = new BinaryWriter(new MemoryStream());
            BinaryWriter plantData = new BinaryWriter(new MemoryStream());
            BinaryWriter rockRubbleData = new BinaryWriter(new MemoryStream());

            int cells = map.info.NumCells;
            for (int i = 0; i < cells; i++)
            {
                IntVec3 cell = map.cellIndices.IndexToCell(i);
                SaveRock(map, rockData, cell);
                SaveRockRubble(map, rockRubbleData, cell);
                SavePlants(map, plantData, cell);
            }

            SaveBinary(rockData, "compressedRocks");
            SaveBinary(plantData, "compressedPlants");
            SaveBinary(rockRubbleData, "compressedRockRubble");
        }

        private static void SaveRock(Map map, BinaryWriter writer, IntVec3 cell)
        {
            Thing thing = map.thingGrid.ThingsListAt(cell).Find(IsSaveRock);

            if (thing != null)
            {
                writer.Write(thing.def.shortHash);
                writer.Write(thing.thingIDNumber);
            }
            else
            {
                writer.Write((ushort)0);
            }
        }

        private static void SaveRockRubble(Map map, BinaryWriter writer, IntVec3 cell)
        {
            Filth thing = (Filth)map.thingGrid.ThingsListAt(cell).Find(IsSaveRockRubble);

            if (thing != null)
            {
                writer.Write(thing.def.shortHash);
                writer.Write(thing.thingIDNumber);
                writer.Write((byte)thing.thickness);
                writer.Write(thing.growTick);
            }
            else
            {
                writer.Write((ushort)0);
            }
        }

        private static void SavePlants(Map map, BinaryWriter writer, IntVec3 cell)
        {
            Plant thing = (Plant)map.thingGrid.ThingsListAt(cell).Find(IsSavePlant);

            if (thing != null)
            {
                writer.Write(thing.def.shortHash);
                writer.Write(thing.thingIDNumber);
                writer.Write(thing.HitPoints);

                writer.Write(thing.Growth);
                writer.Write(thing.Age);

                bool saveNext = thing.unlitTicks != 0 || thing.madeLeaflessTick != -99999;
                byte field = (byte)(thing.sown ? 1 : 0);
                field |= (byte)(saveNext ? 2 : 0);
                writer.Write(field);

                if (saveNext)
                {
                    writer.Write(thing.unlitTicks);
                    writer.Write(thing.madeLeaflessTick);
                }
            }
            else
            {
                writer.Write((ushort)0);
            }
        }

        public static void Load(Map map)
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;

            thingDefsByShortHash = new Dictionary<ushort, ThingDef>();

            foreach (ThingDef thingDef in DefDatabase<ThingDef>.AllDefs)
                thingDefsByShortHash[thingDef.shortHash] = thingDef;

            BinaryReader rockData = LoadBinary("compressedRocks");
            BinaryReader plantData = LoadBinary("compressedPlants");
            BinaryReader rockRubbleData = LoadBinary("compressedRockRubble");
            List<Thing> loadedThings = new List<Thing>();

            int cells = map.info.NumCells;
            for (int i = 0; i < cells; i++)
            {
                IntVec3 cell = map.cellIndices.IndexToCell(i);
                Thing t;

                if ((t = LoadRock(map, rockData, cell)) != null) loadedThings.Add(t);
                if ((t = LoadRockRubble(map, rockRubbleData, cell)) != null) loadedThings.Add(t);
                if ((t = LoadPlants(map, plantData, cell)) != null) loadedThings.Add(t);
            }

            map.GetComponent<MultiplayerMapComp>().loadedThings = loadedThings;
        }

        private static Thing LoadRock(Map map, BinaryReader reader, IntVec3 cell)
        {
            ushort defId = reader.ReadUInt16();
            if (defId == 0)
                return null;

            int id = reader.ReadInt32();
            ThingDef def = thingDefsByShortHash[defId];

            Thing thing = (Thing)Activator.CreateInstance(def.thingClass);
            thing.def = def;
            thing.HitPoints = thing.MaxHitPoints;

            thing.thingIDNumber = id;
            thing.SetPositionDirect(cell);

            return thing;
        }

        private static Thing LoadRockRubble(Map map, BinaryReader reader, IntVec3 cell)
        {
            ushort defId = reader.ReadUInt16();
            if (defId == 0)
                return null;

            int id = reader.ReadInt32();
            byte thickness = reader.ReadByte();
            int growTick = reader.ReadInt32();
            ThingDef def = thingDefsByShortHash[defId];

            Filth thing = (Filth)Activator.CreateInstance(def.thingClass);
            thing.def = def;

            thing.thingIDNumber = id;
            thing.thickness = thickness;
            thing.growTick = growTick;

            thing.SetPositionDirect(cell);
            return thing;
        }

        private static Thing LoadPlants(Map map, BinaryReader reader, IntVec3 cell)
        {
            ushort defId = reader.ReadUInt16();
            if (defId == 0)
                return null;

            int id = reader.ReadInt32();
            int hitPoints = reader.ReadInt32();
            float growth = reader.ReadSingle();
            int age = reader.ReadInt32();

            byte field = reader.ReadByte();
            bool sown = (field & 1) != 0;
            bool loadNext = (field & 2) != 0;

            int plantUnlitTicks = 0;
            int plantMadeLeaflessTick = -99999;
            if (loadNext)
            {
                plantUnlitTicks = reader.ReadInt32();
                plantMadeLeaflessTick = reader.ReadInt32();
            }

            ThingDef def = thingDefsByShortHash[defId];

            Plant thing = (Plant)Activator.CreateInstance(def.thingClass);
            thing.def = def;
            thing.thingIDNumber = id;
            thing.HitPoints = hitPoints;

            thing.InitializeComps();

            thing.Growth = growth;
            thing.Age = age;
            thing.unlitTicks = plantUnlitTicks;
            thing.madeLeaflessTick = plantMadeLeaflessTick;
            thing.sown = sown;

            thing.SetPositionDirect(cell);
            return thing;
        }

        public static bool IsSaveRock(Thing t)
        {
            return t.def.saveCompressible && (!t.def.useHitPoints || t.HitPoints == t.MaxHitPoints);
        }

        private static readonly HashSet<string> savePlants = new HashSet<string>() { "PlantGrass", "PlantTallGrass", "PlantTreeOak", "PlantTreePoplar", "PlantTreeBirch", "PlantTreePine", "PlantBush", "PlantBrambles", "PlantDandelion", "PlantRaspberry", "PlantMoss", "PlantSaguaroCactus", "PlantShrubLow", "PlantTreeWillow", "PlantTreeCypress", "PlantTreeMaple", "PlantChokevine", "PlantWildHealroot" };
        public static bool IsSavePlant(Thing t)
        {
            return savePlants.Contains(t.def.defName);
        }

        public static bool IsSaveRockRubble(Thing t)
        {
            return t.def == ThingDefOf.RockRubble;
        }

        private static void SaveBinary(BinaryWriter writer, string label)
        {
            byte[] arr = (writer.BaseStream as MemoryStream).ToArray();
            DataExposeUtility.ByteArray(ref arr, label);
        }

        private static BinaryReader LoadBinary(string label)
        {
            byte[] arr = null;
            DataExposeUtility.ByteArray(ref arr, label);
            return new BinaryReader(new MemoryStream(arr));
        }
    }

    [HarmonyPatch(typeof(MapFileCompressor))]
    [HarmonyPatch(nameof(MapFileCompressor.BuildCompressedString))]
    public static class SaveCompressPatch
    {
        static bool Prefix(MapFileCompressor __instance)
        {
            if (!SaveCompression.doSaveCompression) return true;
            SaveCompression.Save(__instance.map);
            return false;
        }
    }

    [HarmonyPatch(typeof(MapFileCompressor))]
    [HarmonyPatch(nameof(MapFileCompressor.ExposeData))]
    public static class SaveDecompressPatch
    {
        static bool Prefix(MapFileCompressor __instance)
        {
            if (!SaveCompression.doSaveCompression) return true;
            SaveCompression.Load(__instance.map);
            return false;
        }
    }

    [HarmonyPatch(typeof(MapFileCompressor))]
    [HarmonyPatch(nameof(MapFileCompressor.ThingsToSpawnAfterLoad))]
    public static class DecompressedThingsPatch
    {
        static void Postfix(MapFileCompressor __instance, ref IEnumerable<Thing> __result)
        {
            if (!SaveCompression.doSaveCompression) return;

            MultiplayerMapComp comp = __instance.map.GetComponent<MultiplayerMapComp>();
            __result = comp.loadedThings;
            comp.loadedThings = null;
        }
    }

    [HarmonyPatch(typeof(CompressibilityDeciderUtility))]
    [HarmonyPatch(nameof(CompressibilityDeciderUtility.IsSaveCompressible))]
    public static class SaveCompressiblePatch
    {
        static void Postfix(Thing t, ref bool __result)
        {
            if (!SaveCompression.doSaveCompression) return;

            __result = SaveCompression.IsSavePlant(t) || SaveCompression.IsSaveRock(t) || SaveCompression.IsSaveRockRubble(t);
        }
    }
}
