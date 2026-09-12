using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.IO.Filters;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

public class FilterTests
{
    private const string FlateText = "Hello, PDF filters!Hello, PDF filters!Hello, PDF filters!";

    private static readonly byte[] FlateZlib =
    [
        0x78, 0xDA, 0xF3, 0x48, 0xCD, 0xC9, 0xC9, 0xD7, 0x51, 0x08, 0x70, 0x71, 0x53, 0x48, 0xCB,
        0xCC, 0x29, 0x49, 0x2D, 0x2A, 0x56, 0xF4, 0x20, 0x4A, 0x08, 0x00, 0x23, 0xBC, 0x12, 0xFD,
    ];

    private static readonly byte[] FlateRawDeflate =
    [
        0xF3, 0x48, 0xCD, 0xC9, 0xC9, 0xD7, 0x51, 0x08, 0x70, 0x71, 0x53, 0x48, 0xCB, 0xCC, 0x29,
        0x49, 0x2D, 0x2A, 0x56, 0xF4, 0x20, 0x4A, 0x08, 0x00,
    ];

    [Fact]
    public void Decodes_a_flate_stream()
    {
        Text(Decode(FlateZlib, PdfName.FlateDecode)).ShouldBe(FlateText);
    }

    [Fact]
    public void Decodes_a_flate_stream_that_lost_its_zlib_header()
    {
        var diagnostics = new PdfDiagnostics();

        Text(Decode(FlateRawDeflate, PdfName.FlateDecode, diagnostics: diagnostics)).ShouldBe(FlateText);

        diagnostics.HasRepairs.ShouldBeTrue();
    }

    [Fact]
    public void Keeps_what_it_could_decode_of_a_truncated_flate_stream()
    {
        var diagnostics = new PdfDiagnostics();
        var truncated = FlateZlib.AsSpan(0, 20).ToArray();

        var decoded = Text(Decode(truncated, PdfName.FlateDecode, diagnostics: diagnostics));

        decoded.ShouldNotBeEmpty();
        FlateText.ShouldStartWith(decoded);
    }

    [Fact]
    public void Leaves_data_alone_when_flate_cannot_decode_anything()
    {
        var diagnostics = new PdfDiagnostics();
        byte[] garbage = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06];

        Decode(garbage, PdfName.FlateDecode, diagnostics: diagnostics).ToArray().ShouldBe(garbage);

        diagnostics.Contains(PdfDiagnosticCodes.FilterFailed).ShouldBeTrue();
    }

    [Fact]
    public void Decodes_a_hexadecimal_stream()
    {
        Text(Decode("48656C6C6F>"u8.ToArray(), PdfName.ASCIIHexDecode)).ShouldBe("Hello");
    }

    [Fact]
    public void Pads_an_odd_hexadecimal_stream()
    {
        Decode("4A5>"u8.ToArray(), PdfName.ASCIIHexDecode).ToArray().ShouldBe([0x4A, 0x50]);
    }

    [Fact]
    public void Decodes_an_ascii85_stream()
    {
        Text(Decode("87cURD]i,\"Ebo7~>"u8.ToArray(), PdfName.ASCII85Decode)).ShouldBe("Hello World");
    }

    [Fact]
    public void Reads_the_z_shortcut_of_an_ascii85_stream()
    {
        Decode("z~>"u8.ToArray(), PdfName.ASCII85Decode).ToArray().ShouldBe([0, 0, 0, 0]);
    }

    [Fact]
    public void Decodes_a_run_length_stream()
    {
        // Two literal bytes, then five copies of 0x41, then the end marker.
        byte[] encoded = [0x01, 0x48, 0x49, 0xFC, 0x41, 0x80];

        Text(Decode(encoded, PdfName.RunLengthDecode)).ShouldBe("HIAAAAA");
    }

    [Fact]
    public void Decodes_the_lzw_example_from_the_specification()
    {
        byte[] encoded = [0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01];

        Text(Decode(encoded, PdfName.LZWDecode)).ShouldBe("-----A---B");
    }

    [Fact]
    public void Undoes_a_png_up_predictor()
    {
        byte[] predicted = [0x02, 0x0A, 0x14, 0x1E, 0x28, 0x02, 0x01, 0x02, 0x03, 0x04, 0x02, 0x01, 0x02, 0x03, 0x04];

        var result = PredictorTransform.Apply(predicted, predictor: 12, colors: 1, bitsPerComponent: 8, columns: 4);

        result.ShouldBe([10, 20, 30, 40, 11, 22, 33, 44, 12, 24, 36, 48]);
    }

    [Fact]
    public void Undoes_a_tiff_predictor()
    {
        byte[] predicted = [10, 5, 5, 5];

        var result = PredictorTransform.Apply(predicted, predictor: 2, colors: 1, bitsPerComponent: 8, columns: 4);

        result.ShouldBe([10, 15, 20, 25]);
    }

    [Fact]
    public void Applies_a_chain_of_filters_in_order()
    {
        // The data is flate-encoded first, then hex-encoded, so decoding runs hex then flate.
        var hex = Convert.ToHexString(FlateZlib) + ">";
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray([PdfName.ASCIIHexDecode, PdfName.FlateDecode]));
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(Encoding.ASCII.GetBytes(hex)));

        Text(stream.Decode()).ShouldBe(FlateText);
    }

    [Fact]
    public void Leaves_an_image_filter_encoded()
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, PdfName.DCTDecode);
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0];
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(jpeg));

        stream.HasImageFilter().ShouldBeTrue();
        stream.Decode().ToArray().ShouldBe(jpeg);
    }

    [Fact]
    public void Reports_a_filter_it_does_not_know()
    {
        var diagnostics = new PdfDiagnostics();

        Decode([1, 2, 3], PdfName.Get("MadeUpDecode"), diagnostics: diagnostics);

        diagnostics.Contains(PdfDiagnosticCodes.FilterUnsupported).ShouldBeTrue();
    }

    [Fact]
    public void Returns_raw_data_when_no_filter_is_declared()
    {
        var stream = new PdfStream(new PdfDictionary(), PdfStreamData.FromMemory("plain"u8.ToArray()));

        Text(stream.Decode()).ShouldBe("plain");
    }

    private static ReadOnlyMemory<byte> Decode(
        byte[] data,
        PdfName filter,
        PdfDictionary? parameters = null,
        PdfDiagnostics? diagnostics = null)
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, filter);

        if (parameters is not null)
        {
            dictionary.Set(PdfName.DecodeParms, parameters);
        }

        return new PdfStream(dictionary, PdfStreamData.FromMemory(data)).Decode(diagnostics);
    }

    private static string Text(ReadOnlyMemory<byte> data) => Encoding.ASCII.GetString(data.Span);
}
