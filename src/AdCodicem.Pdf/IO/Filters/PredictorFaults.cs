namespace AdCodicem.Pdf.IO.Filters;

/// <summary>What a predictor's data holds that the predictor's format does not allow, as undoing it found it.</summary>
/// <param name="Partial">
/// How many bytes of a last row the data holds in part, a PNG row's filter type among them, where the data was not cut;
/// 0 when it holds none.
/// </param>
/// <param name="Stride">How many bytes a whole row takes in the data, a PNG row's filter type among them.</param>
/// <param name="Rows">How many PNG rows were undone, a last one held in part among them.</param>
/// <param name="UnknownTags">How many PNG rows name a filter type past 4, which PNG does not define.</param>
/// <param name="FirstUnknownRow">The first such row, counted from 1; 0 when there is none.</param>
/// <param name="FirstUnknownTag">The filter type the first such row names.</param>
internal readonly record struct PredictorFaults(
    int Partial, long Stride, int Rows, int UnknownTags, int FirstUnknownRow, int FirstUnknownTag);
