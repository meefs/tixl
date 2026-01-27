namespace Lib.render.gizmo;

/// <summary>
/// Generates points for drawing a wireframe direction arrow visualization.
/// Creates a main line pointing in the forward direction (-Z) with 4 diagonal lines
/// pointing back at a 45 degree angle to form an arrow head appearance.
/// Output points can be fed into DrawLines for rendering.
/// </summary>
[Guid("6cfb3330-ebcc-4ddc-861b-ad379ad55f4b")]
internal sealed class DirectionArrowGizmo : Instance<DirectionArrowGizmo>
{
    [Output(Guid = "8812a6d5-7430-449c-beb6-4240939b56a9")]
    public readonly Slot<StructuredList> Points = new();

    public DirectionArrowGizmo()
    {
        Points.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var length = Length.GetValue(context);
        var headLength = HeadLength.GetValue(context);
        var headSpread = HeadSpread.GetValue(context);

        // get head count and angle
        var headLineCount = HeadLineCount.GetValue(context);
        var headAngleDeg = HeadAngle.GetValue(context);

        // clamp values to safe ranges
        if (headLineCount < 0) headLineCount = 0;

        // Convert head angle (degrees) to radians for MathF trig functions
        var headAngleRad = headAngleDeg * (MathF.PI / 180f);

        // Arrow points forward in -Z direction
        // Main shaft: from origin to -Z
        // Head lines: N lines at specified angle pointing back
        
        // Calculate head offset (angle back from tip)
        var headBackZ = headLength * MathF.Cos(headAngleRad);
        var headSideOffset = headLength * MathF.Sin(headAngleRad) * headSpread;
        
        // Tip position (end of arrow)
        var tipZ = -length;
        
        // Head base position (where diagonal lines start going back)
        var headBaseZ = tipZ + headBackZ;
        
        // Total points needed:
        // - Main shaft: 2 points + separator = 3
        // - headLineCount head lines: each 2 points + separator = 3 each
        var totalPoints = 3 + headLineCount * 3;
        
        if (_pointList == null || _pointList.NumElements != totalPoints)
        {
            _pointList = new StructuredList<Point>(totalPoints);
        }
        
        var points = _pointList.TypedElements;
        var index = 0;
        
        // Main shaft line (origin to tip)
        points[index++] = new Point
        {
            Position = Vector3.Zero,
            F1 = 1f,
            Color = Vector4.One
        };
        points[index++] = new Point
        {
            Position = new Vector3(0, 0, tipZ),
            F1 = 1f,
            Color = Vector4.One
        };
        points[index++] = Point.Separator();
        
        // N diagonal head lines evenly distributed around the shaft
        if (headLineCount > 0)
        {
            for (int i = 0; i < headLineCount; i++)
            {
                var angle = (i / (float)headLineCount) * MathF.PI * 2f; // evenly spaced around circle
                var x = MathF.Cos(angle) * headSideOffset;
                var y = MathF.Sin(angle) * headSideOffset;
                
                // Line from tip to the head base position offset by x,y
                points[index++] = new Point
                {
                    Position = new Vector3(0, 0, tipZ),
                    F1 = 1f,
                    Color = Vector4.One
                };
                points[index++] = new Point
                {
                    Position = new Vector3(x, y, headBaseZ),
                    F1 = 1f,
                    Color = Vector4.One
                };
                points[index++] = Point.Separator();
            }
        }
        
        Points.Value = _pointList;
    }

    private StructuredList<Point> _pointList;

    /// <summary>Total length of the arrow from origin to tip.</summary>
    [Input(Guid = "c3960c08-1449-4376-a26a-cd4d5bea7e5c")]
    public readonly InputSlot<float> Length = new();

    /// <summary>Length of the arrow head diagonal lines.</summary>
    [Input(Guid = "6b30f270-e473-41b1-ba5b-ee6006a640e0")]
    public readonly InputSlot<float> HeadLength = new();

    /// <summary>Spread multiplier for the arrow head width (1.0 = 45 degree spread).</summary>
    [Input(Guid = "58f50fa0-5b76-4db2-bc11-0bfd380284f2")]
    public readonly InputSlot<float> HeadSpread = new();

    /// <summary>Number of diagonal head lines around the shaft.</summary>
    [Input(Guid = "685652ca-db7f-4bd9-bed5-6601e6c1ad4d")]
    public readonly InputSlot<int> HeadLineCount = new();

    /// <summary>Angle (in degrees) between the diagonal head lines and the shaft (e.g. 45).</summary>
    [Input(Guid = "2553df61-b57c-4de9-948d-0d1eab572098")]
    public readonly InputSlot<float> HeadAngle = new();
}
