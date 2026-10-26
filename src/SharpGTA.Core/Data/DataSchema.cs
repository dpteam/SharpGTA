using System;

namespace SharpGTA.Data
{
    /// <summary>
    /// Immutable text-data limits and the layout differences between GTA III and Vice City.
    /// </summary>
    public sealed class DataSchema
    {
        public const int DefaultMaxLineLength = 4096;
        public const int DefaultMaxTokens = 128;
        public const int DefaultMaxRecords = 1000000;

        private static readonly DataSchema Iii = new DataSchema(false, false, false, false, false,
            DataSchema.DefaultMaxLineLength, DataSchema.DefaultMaxTokens, DataSchema.DefaultMaxRecords,
            false, true, false);
        private static readonly DataSchema ViceCity = new DataSchema(true, true, true, true, true,
            DataSchema.DefaultMaxLineLength, DataSchema.DefaultMaxTokens, DataSchema.DefaultMaxRecords,
            true, false, true);

        public DataSchema(
            bool vehicleHasAnimFile,
            bool pedHasAnimAndRadio,
            bool supportsWeaponSection,
            bool allowInstanceArea,
            bool numericPathTypes,
            int maxLineLength = DefaultMaxLineLength,
            int maxTokens = DefaultMaxTokens,
            int maxRecords = DefaultMaxRecords,
            bool supportsOcclusion = false,
            bool supportsMapZoneDirective = false,
            bool hasSeparateZoneArrays = false)
        {
            if (maxLineLength <= 0)
                throw new ArgumentOutOfRangeException("maxLineLength");
            if (maxTokens <= 0)
                throw new ArgumentOutOfRangeException("maxTokens");
            if (maxRecords <= 0)
                throw new ArgumentOutOfRangeException("maxRecords");

            VehicleHasAnimFile = vehicleHasAnimFile;
            PedHasAnimAndRadio = pedHasAnimAndRadio;
            SupportsWeaponSection = supportsWeaponSection;
            AllowInstanceArea = allowInstanceArea;
            NumericPathTypes = numericPathTypes;
            MaxLineLength = maxLineLength;
            MaxTokens = maxTokens;
            MaxRecords = maxRecords;
            SupportsOcclusion = supportsOcclusion;
            SupportsMapZoneDirective = supportsMapZoneDirective;
            HasSeparateZoneArrays = hasSeparateZoneArrays;
        }

        public static DataSchema III
        {
            get { return Iii; }
        }

        public static DataSchema VC
        {
            get { return ViceCity; }
        }

        public bool VehicleHasAnimFile { get; private set; }
        public bool PedHasAnimAndRadio { get; private set; }
        public bool SupportsWeaponSection { get; private set; }
        public bool AllowInstanceArea { get; private set; }
        public bool NumericPathTypes { get; private set; }
        public int MaxLineLength { get; private set; }
        public int MaxTokens { get; private set; }
        public int MaxRecords { get; private set; }
        public bool SupportsOcclusion { get; private set; }
        public bool SupportsMapZoneDirective { get; private set; }
        public bool HasSeparateZoneArrays { get; private set; }
    }
}
