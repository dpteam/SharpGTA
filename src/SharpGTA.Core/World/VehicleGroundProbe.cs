using System;
using System.Collections.Generic;
using System.Globalization;
using SharpGTA.Mathematics;

namespace SharpGTA.World
{
    /// <summary>
    /// How far the spawned vehicles sit from the COLL surface below them. The path node carries
    /// the lane plane, so a positive delta means a car floats above its road; a negative one is
    /// normal on multi-level roads and tunnels, where the surface above is not the one the car
    /// is driving on.
    /// </summary>
    public sealed class VehicleGroundReport
    {
        public int Measured { get; internal set; }
        public int Misses { get; internal set; }
        public float Minimum { get; internal set; }
        public float Maximum { get; internal set; }
        public float Average { get; internal set; }
    }

    public static class VehicleGroundProbe
    {
        public static VehicleGroundReport Measure(StaticWorld world)
        {
            if (world == null)
                throw new ArgumentNullException("world");
            VehicleGroundReport report = new VehicleGroundReport();
            report.Minimum = float.MaxValue;
            report.Maximum = float.MinValue;
            IList<WorldEntity> entities = world.DynamicEntities;
            float total = 0.0f;
            for (int i = 0; i < entities.Count; i++)
            {
                DynamicEntity entity = entities[i] as DynamicEntity;
                if (entity == null || entity.Kind != WorldEntityKind.Vehicle)
                    continue;
                StaticWorldHit hit;
                if (!world.QueryVertical(entity.Position.X, entity.Position.Y,
                    entity.Position.Z + 20.0f, entity.Position.Z - 60.0f, out hit))
                {
                    report.Misses++;
                    continue;
                }
                float delta = entity.Position.Z - hit.Position.Z;
                if (delta < report.Minimum)
                    report.Minimum = delta;
                if (delta > report.Maximum)
                    report.Maximum = delta;
                total += delta;
                report.Measured++;
            }
            if (report.Measured == 0)
            {
                report.Minimum = 0.0f;
                report.Maximum = 0.0f;
                return report;
            }
            report.Average = total / report.Measured;
            return report;
        }

        /// <summary>Diagnostic line shared by headless and the D3D9 preview.</summary>
        public static string Format(VehicleGroundReport report)
        {
            if (report == null)
                throw new ArgumentNullException("report");
            return "Path-Follow-Ground: measured=" + report.Measured.ToString(CultureInfo.InvariantCulture) +
                " misses=" + report.Misses.ToString(CultureInfo.InvariantCulture) +
                " deltaMin=" + report.Minimum.ToString("0.###", CultureInfo.InvariantCulture) +
                " deltaMax=" + report.Maximum.ToString("0.###", CultureInfo.InvariantCulture) +
                " deltaAvg=" + report.Average.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}