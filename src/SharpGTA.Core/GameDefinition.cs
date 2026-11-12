using System;
using SharpGTA.Data;

namespace SharpGTA
{
    public enum GameId
    {
        III = 3,
        ViceCity = 6
    }

    public enum CollisionStorageKind
    {
        LevelBased,
        StreamedSlots
    }

    /// <summary>Limits from one game's data/layout profile, never shared raw enum values.</summary>
    public sealed class GameLimits
    {
        public GameLimits(int modelInfo, int txdStore, int animationBlocks, int animations,
            int pathNodes, int pathConnections, int pedGroups, int modelsPerPedGroup,
            int buildings, int objects, int peds, int vehicles, int dummies, int treadables)
        {
            if (modelInfo <= 0 || txdStore <= 0 || animationBlocks <= 0 || animations <= 0 ||
                pathNodes <= 0 || pathConnections <= 0 || pedGroups <= 0 || modelsPerPedGroup <= 0 ||
                buildings <= 0 || objects <= 0 || peds <= 0 || vehicles <= 0 || dummies <= 0 ||
                treadables <= 0)
                throw new ArgumentOutOfRangeException("modelInfo", "Game limits must be positive.");
            ModelInfo = modelInfo;
            TxdStore = txdStore;
            AnimationBlocks = animationBlocks;
            Animations = animations;
            PathNodes = pathNodes;
            PathConnections = pathConnections;
            PedGroups = pedGroups;
            ModelsPerPedGroup = modelsPerPedGroup;
            Buildings = buildings;
            Objects = objects;
            Peds = peds;
            Vehicles = vehicles;
            Dummies = dummies;
            Treadables = treadables;
        }

        public int ModelInfo { get; private set; }
        public int TxdStore { get; private set; }
        public int AnimationBlocks { get; private set; }
        public int Animations { get; private set; }
        public int PathNodes { get; private set; }
        public int PathConnections { get; private set; }
        public int PedGroups { get; private set; }
        public int ModelsPerPedGroup { get; private set; }

        /// <summary>CBuildingPool size (NUMBUILDINGS).</summary>
        public int Buildings { get; private set; }

        /// <summary>CObjectPool size (NUMOBJECTS).</summary>
        public int Objects { get; private set; }

        /// <summary>CPedPool size (NUMPEDS).</summary>
        public int Peds { get; private set; }

        /// <summary>CVehiclePool size (NUMVEHICLES).</summary>
        public int Vehicles { get; private set; }

        /// <summary>CDummyPool size (NUMDUMMIES).</summary>
        public int Dummies { get; private set; }

        /// <summary>CTreadablePool size (NUMTREADABLES).</summary>
        public int Treadables { get; private set; }
    }

    /// <summary>
    /// Immutable identity, file locations and data rules for one game. Game-specific
    /// projects derive from this class; Core never selects III/VC by raw numeric IDs.
    /// </summary>
    public abstract class GameDefinition
    {
        protected GameDefinition(GameId id, string displayName, string mainDatPath,
            string initialLevel, int maxModelName, int weatherCount,
            CollisionStorageKind collisionStorage, DataSchema dataSchema, GameLimits limits)
        {
            if (displayName == null)
                throw new ArgumentNullException("displayName");
            if (mainDatPath == null)
                throw new ArgumentNullException("mainDatPath");
            if (initialLevel == null)
                throw new ArgumentNullException("initialLevel");
            if (dataSchema == null)
                throw new ArgumentNullException("dataSchema");
            if (limits == null)
                throw new ArgumentNullException("limits");
            Id = id;
            DisplayName = displayName;
            MainDatPath = mainDatPath;
            InitialLevel = initialLevel;
            MaxModelName = maxModelName;
            WeatherCount = weatherCount;
            CollisionStorage = collisionStorage;
            DataSchema = dataSchema;
            Limits = limits;
        }

        public GameId Id { get; private set; }
        public string DisplayName { get; private set; }
        public string DefaultDatPath { get { return "DATA/DEFAULT.DAT"; } }
        public string MainDatPath { get; private set; }
        public string InitialImagePath { get { return "MODELS/GTA3.IMG"; } }
        public string InitialLevel { get; private set; }
        public int MaxModelName { get; private set; }
        public int WeatherCount { get; private set; }
        public CollisionStorageKind CollisionStorage { get; private set; }
        public DataSchema DataSchema { get; private set; }
        public GameLimits Limits { get; private set; }
    }
}
