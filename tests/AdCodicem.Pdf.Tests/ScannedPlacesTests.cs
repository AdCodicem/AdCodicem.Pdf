using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// What the rebuild's scan for objects takes of a file is recorded once a document, the first time it is asked for, whatever
/// was read before; a rebuild takes its headers from that record where one was made rather than scanning the file again, and
/// makes none of its own.
/// </summary>
public class ScannedPlacesTests
{
    private const string Catalog = "<< /Type /Catalog /Pages 2 0 R >>";
    private const string Pages = "<< /Type /Pages /Kids [] /Count 0 >>";

    [Theory]
    [InlineData("a header between objects", "Clean")]
    [InlineData("a header whose number is written with a sign", "Clean")]
    [InlineData("a header in a comment between objects", null)]
    [InlineData("a header in a string between objects", null)]
    [InlineData("a header in the data of a stream whose length an endstream confirms", null)]
    [InlineData("a header in the data of a stream whose length is an object", null)]
    [InlineData("a header in the data of a stream whose length's object has each token on a line of its own", null)]
    [InlineData("a header in the data of a stream whose length is an object no endstream follows the length of", "Raw")]
    [InlineData("a header in the data of a stream whose length's object lies more than a mebibyte past its endstream", "Raw")]
    [InlineData("a header in an attached PDF file whose header 59 bytes of white space precede, its length's object past the object after it", null)]
    [InlineData("a header in the data of a stream whose PDF header 60 bytes of white space precede, its length's object past the object after it", "Raw")]
    [InlineData("a header in the data of a stream whose subtype, not its type, is EmbeddedFile, its length's object past the object after it", "Raw")]
    [InlineData("a header in the data of a stream whose length names object 0, which a header past the data's first endstream holds", "Raw")]
    [InlineData("a header past the first endstream of an attached PDF file with no type whose direct length an endobj follows, its endstream lost", "Raw")]
    [InlineData("a header in the data of a stream with no endstream, in that of a stream whose length runs past the end of the file, in that of a stream whose length's object is lost", "Raw")]
    [InlineData("a header on a comment line that holds an object before it, past where a load ends the data of a stream with no endstream", "Raw")]
    [InlineData("a header on the line of the endstream of a stream whose data opens a string, past where a load ends the data of a stream with no endstream", "Clean")]
    [InlineData("a header on the line of a comment that holds the first endstream of a stream with no endstream, past such a stream whose first endstream's line ends before it", "Raw")]
    [InlineData("a header in the data of a stream with no endstream whose length's object is lost, in an attached file's data whose length's object is lost", "Raw")]
    [InlineData("a header in the data of a stream with no endstream, in that of a stream whose length's object, in its data, ends it past that stream's start", "Raw")]
    [InlineData("a header past an attached PDF file's object of its length's number, past the file's first endstream and within the data that length gives", "Clean")]
    [InlineData("a header in the data of a stream whose header an attached file's data holds, the attached file's length's object after both ending it there", "Raw")]
    [InlineData("a header past an attached PDF file that lost its endstream and endobj, its direct length ending where a reference follows", "Raw")]
    [InlineData("a header on the line a string that runs over a line ends, past the line of a comment that holds the first endstream of a stream with no endstream", "Clean")]
    [InlineData("a header on the line a string that runs over two lines ends, past the line of a comment that holds the first endstream of a stream with no endstream", "Clean")]
    [InlineData("a header on the line a string that runs over a line ends, past the line of the first endstream of a stream with no endstream whose length's object follows", "Clean")]
    [InlineData("a header past two xref keywords that start their lines, between a stream with no endstream, whose length's object is lost, and the next header", "Clean")]
    [InlineData("a header in an attached PDF file whose length's object before it holds a length past the end of the file, its endstream there", "Raw")]
    [InlineData("a header past an attached PDF file that lost its endstream and endobj, whose length's object after it ends its data where that object starts", "Clean")]
    [InlineData("a header past an attached PDF file that lost its endstream and endobj, its direct length ending where a word and a number follow", "Raw")]
    [InlineData("a header on the line a string that runs over a line ends, its first byte a mebibyte's sixteenth and a byte past the object that opens it", "Clean")]
    [InlineData("a header on the line of a comment that holds the first endstream of an attached file that lost its endstream, the header of its length's object", "Clean")]
    [InlineData("a header in the data of a stream with no length, on the line of its first endstream, in the data of a stream whose length's object is lost", "Raw")]
    [InlineData("a header glued to a sign past an xref keyword that starts its line, in a stream with no endstream, a stream glued to a sign past it", "Raw")]
    [InlineData("a header in the data of a stream with no endstream whose length's object is lost, written again where that stream's data starts", "Raw")]
    [InlineData("a header in the data of a stream whose length's object's obj ends a mebibyte past the data's first endstream", null)]
    [InlineData("a header in the data of a stream whose length's object's obj ends a mebibyte and a byte past the data's first endstream", "Raw")]
    [InlineData("a header in the data of a stream whose length's object's number starts 33 bytes past the data's first endstream, its obj 62", null)]
    [InlineData("a header in the data of a stream whose length's object's number starts 32 bytes past the data's first endstream, its obj 62", "Raw")]
    [InlineData("a header after another in the data of an intact stream whose indirect length's object follows it", null)]
    [InlineData("a header in what a string that holds endobj runs into", "Raw")]
    [InlineData("a header past the endobj that follows the declared length of a stream with no endstream", "Clean")]
    [InlineData("a header that follows the declared length of a stream with no endstream", "Clean")]
    [InlineData("a header that follows the declared length of a stream with no endstream, once the scan has read what it may past lengths", null)]
    [InlineData("a header past the endobj that ends a stream with no endstream whose declared length runs into it", "Clean")]
    [InlineData("a header that a stream with no endstream declares a length past", "Raw")]
    [InlineData("a header past the endobj of a stream with no endstream that declares a length past the header", "Clean")]
    [InlineData("a header that holds the indirect length of the stream with no endstream before it", "Clean")]
    [InlineData("a header that holds the indirect length of the stream with no endstream before it, sixty-one objects before the stream", "Clean")]
    [InlineData("a header that holds the indirect length of the stream before it, whose endstream and endobj are lost", "Clean")]
    [InlineData("a header that holds the indirect length of the stream before it, whose endstream and endobj are lost, an endstream in a string past it", "Clean")]
    [InlineData("a header past a stream with no endstream whose indirect length the object before the stream holds", "Clean")]
    [InlineData("a header past a stream with no endstream or endobj whose indirect length the object before it holds", "Clean")]
    [InlineData("a header past where the value before a stream ends its data, the value's endobj ending 63 bytes past its header", "Clean")]
    [InlineData("a header in data the value before its stream would end before it, the value's endobj ending 64 bytes past its header", "Raw")]
    [InlineData("a header past where the value before a stream ends its data, white space a first read after that end holds between", "Clean")]
    [InlineData("a header in data the value before its stream would end before it, white space longer than a first read after that end between", "Raw")]
    [InlineData("a header past a stream with no endstream whose indirect length names an object the file lacks", "Clean")]
    [InlineData("a header past a stream with no endstream or endobj whose indirect length names an object the file lacks", "Clean")]
    [InlineData("a header in the data of an intact stream whose indirect length names an object the file lacks", "Raw")]
    [InlineData("a header in the data of a stream with no endstream whose indirect length names a negative object number", "Raw")]
    [InlineData("a header in the data of a stream with no endstream whose indirect length names an object number past the largest", "Raw")]
    [InlineData("a header in the data of a stream with no endstream whose indirect length names a negative generation", "Raw")]
    [InlineData("a header in the data of a stream with no endstream whose indirect length names a generation past the largest", "Raw")]
    [InlineData("a header in the data of an intact stream that the length an object of its length's number holds runs past", "Raw")]
    [InlineData("a header past a stream with no endstream or endobj whose length names another generation than the object past it", "Clean")]
    [InlineData("a header in the data of an intact stream whose length names an object that holds a real number", "Raw")]
    [InlineData("a header in the data of an intact stream whose length names an object that holds a negative number", "Raw")]
    [InlineData("a header past the endobj of a stream with no endstream whose indirect length runs past the header", "Clean")]
    [InlineData("a header that a stream with no endstream declares an indirect length past", "Raw")]
    [InlineData("a header after an endobj in the data of a stream whose indirect length the object before it holds", null)]
    [InlineData("a header in a file the data of a stream holds whose indirect length the object before it holds", null)]
    [InlineData("a header past the object whose header a stream with no endstream declares a length past", "Clean")]
    [InlineData("a header past the object whose header a stream with no endstream declares an indirect length past", "Clean")]
    [InlineData("a header past the object that holds the indirect length its stream with no endstream declares past it", "Clean")]
    [InlineData("a header past the endobj of a stream with no endstream that declares a length past the end of the file", "Clean")]
    [InlineData("a header in the data of a stream with no endstream that declares a length past the end of the file", "Raw")]
    [InlineData("a header past the endobj of a stream with no endstream whose indirect length runs past the end of the file", "Clean")]
    [InlineData("a header past the endobj of a stream with no endstream whose indirect length is the largest integer", "Clean")]
    [InlineData("a header past the endobj of a stream with no endstream whose direct length is the largest integer", "Clean")]
    [InlineData("a header on the line of a string the data of a stream with no endstream opens past its declared length", "Raw")]
    [InlineData("a header on the line of a comment the data of a stream with no endstream opens past its declared length", "Raw")]
    [InlineData("a header on the line of a string the data of a stream with no endstream opens past its indirect length", "Raw")]
    [InlineData("a header in a string past the endobj that follows the declared length of a stream with no endstream", "Raw")]
    [InlineData("a header in a comment past the endobj that follows the declared length of a stream with no endstream", "Raw")]
    [InlineData("a header in a string past the endobj that follows the indirect length of a stream with no endstream", "Raw")]
    [InlineData("a header in a comment past the endobj that follows the indirect length of a stream with no endstream", "Raw")]
    [InlineData("a header between a stream with no endstream and the object a few objects on that holds its indirect length", "Clean")]
    [InlineData("a header past a stream with no endstream whose indirect length a value before a stream before it holds", "Clean")]
    [InlineData("a header between a stream with no endstream and a stream before the object that holds its indirect length", "Clean")]
    [InlineData("a header before a stream whose data holds a header of the object that holds the indirect length past both", "Clean")]
    [InlineData("a header in the data of an intact stream whose first header holds an integer as its indirect length's object", null)]
    [InlineData("a header in the data of a stream the data of a stream with no endstream holds, its length's object past both", "Raw")]
    [InlineData("a header in what a string that runs past its object's end runs into, past a stream with no endstream", "Raw")]
    [InlineData("a header past a stream with no endstream, a stream with no endstream whose length is awaited in its turn after it", "Clean")]
    [InlineData("a header past a stream with no endstream whose length's object is where a stream after it resolves its own", "Clean")]
    [InlineData("a header in an attached PDF file that holds an object of its stream's length's number past its first endstream", "Raw")]
    [InlineData("a header in a comment between a stream with no endstream and the object a few objects on that holds its length", "Raw")]
    [InlineData("a header past the first endstream of data a length an earlier object of its number gives cuts, its last line a comment", "Raw")]
    [InlineData("a header past the first endstream of data a length an earlier object of its number gives cuts, its last line a string", "Raw")]
    [InlineData("a header on the line of a comment that holds the first endstream of a stream with no endstream past its declared length", "Raw")]
    [InlineData("a header on the line after a comment that holds the first endstream of a stream with no endstream past its declared length", "Clean")]
    [InlineData("a header on the line of a comment that holds the first endstream of a stream with no endstream, its length's object after it", "Raw")]
    [InlineData("a header on the line of a string that holds the first endstream of a stream with no endstream, its length's object after it", "Raw")]
    [InlineData("a header on the line of a comment that holds the first endstream of a stream with no endstream, its length's object lost", "Raw")]
    [InlineData("a header on the line after a comment that holds the first endstream of a stream with no endstream, its length's object after it", "Clean")]
    [InlineData("a header past an escaped end of line, in a string that holds the first endstream of a stream with no endstream past its length", "Raw")]
    [InlineData("a header past an end of line, after a string that holds the first endstream of a stream with no endstream past its length", "Clean")]
    [InlineData("a header in data a length an earlier object of its number gives cuts, a string a header there a comment holds runs over", null)]
    [InlineData("a header past data a length an earlier object of its number gives cuts, where a stream a comment there holds ends", "Clean")]
    [InlineData("a header past where a load ends the data of a stream whose dictionary, in data a length an earlier object gives cuts, spells endstream", "Clean")]
    [InlineData("a header that starts where the declared length of a stream with no endstream ends", "Clean")]
    [InlineData("a header in the data of a stream whose indirect length's object follows a stream whose first header lies past its endstream", "Clean")]
    [InlineData("a header in the data of a stream whose indirect length's object starts where a stream with no endstream after it declares its data ends", "Clean")]
    [InlineData("a header in the data of a stream whose indirect length a value in the data of a stream the scan does not confirm holds", "Clean")]
    [InlineData("a header in the data of a stream that a walk back over comment lines reads after its length's object, past the data's first endstream", "Raw")]
    [InlineData("a header that holds the indirect length of a stream whose data starts where the direct length of the stream holding it ends", "Clean")]
    [InlineData("a header past what a string that runs past its object's end runs into, in the data of a stream with no length", "Raw")]
    [InlineData("a header past where a stream with no endstream declares its data ends, in the data of a stream with no length", "Raw")]
    [InlineData("a header past an object whose string holds a percent sign, past the endobj that follows the declared length of a stream with no endstream", "Clean")]
    [InlineData("a header on the line an object whose string runs over a line ends, past the endobj that follows the declared length of a stream with no endstream", "Clean")]
    [InlineData("a header on the line an object whose string runs over a line ends, past the endobj of a stream with no endstream whose length's object follows", "Raw")]
    [InlineData("a header in the data of a stream that holds an object of its length's number, which the file lacks", "Clean")]
    [InlineData("a header in an attached PDF file that holds an object of its stream's length's number, which the file lacks", "Raw")]
    [InlineData("a header in an attached PDF file after a blank line, that holds an object of its stream's length's number, which the file lacks", "Raw")]
    [InlineData("a header in the data of a stream of type EmbeddedFile that holds an object of its length's number, which the file lacks", "Raw")]
    [InlineData("a header in an attached FDF file with a stream, its stream's length's object more than eight values before it", "Raw")]
    [InlineData("a header past the first endstream of an attached PDF file that holds an object of its stream's length's number past it", "Raw")]
    [InlineData("a header in a stream's data longer than a mebibyte, its length's object more than eight values before it", "Raw")]
    [InlineData("a header in an attached file's data longer than a mebibyte, its length's object more than eight values before it", "Raw")]
    [InlineData("a header past an attached PDF file that lost its endstream, its length's object after it", "Clean")]
    [InlineData("a header past an attached PDF file that lost its endstream, its length's object past an intact stream after it", "Clean")]
    [InlineData("a header past an attached PDF file that lost its endstream, its length's object before it", "Clean")]
    [InlineData("a header in an attached PDF file whose length's object before it holds a length neither an endstream nor an endobj follows", "Raw")]
    [InlineData("a header in an attached PDF file whose length's object after it holds a length past the end of the file", "Raw")]
    [InlineData("a header past an attached PDF file whose direct length an endobj follows, its endstream lost", "Clean")]
    [InlineData("a header in an attached PDF file whose direct length ends short of its data, no endstream following", "Raw")]
    [InlineData("a header past the first endstream of an attached PDF file whose direct length an endobj follows, its endstream lost", "Raw")]
    [InlineData("a header past the first endstream of an attached PDF file whose length's object before it holds a length an endobj follows, its endstream lost", "Raw")]
    [InlineData("a header past the first endstream of an attached PDF file whose length's object after it holds a length an endobj follows, its endstream lost", "Raw")]
    [InlineData("a header that starts where the direct length of an attached file that lost its endstream and endobj ends", "Clean")]
    [InlineData("a header that starts where the indirect length of an attached file that lost its endstream and endobj ends", "Clean")]
    [InlineData("a header in an attached PDF file whose own object of its length's number holds 0, its length's object after it", "Raw")]
    [InlineData("a header in an attached PDF file whose own object of its length's number ends its data at that header, its length's object lost", "Raw")]
    [InlineData("a header past an attached text that lost its endstream, the first header after it, its length's object past it", "Clean")]
    [InlineData("a header written again and again at one distance past the endobj of a stream with no endstream whose length's object follows", "Clean")]
    [InlineData("a header past a stream with no endstream, past which an attached PDF file holds an object of its length's number past its first endstream", "Clean")]
    [InlineData("a header past where a length written with a negative generation ends the data of a stream with no endstream", "Clean")]
    [InlineData("a header past where a length written with a generation past the largest ends the data of a stream with no endstream", "Clean")]
    [InlineData("a header past a stream with no endstream whose header lies in the data of another, each length's object past both", "Clean")]
    [InlineData("a header glued to a sign past an xref keyword between a stream with no endstream, whose length's object is lost, and the next header", "Clean")]
    [InlineData("a header glued to a sign past a trailer keyword between a stream with no endstream, whose length's object is lost, and the next header", "Clean")]
    [InlineData("a header glued to a sign past a trailer keyword that starts the data of a stream with no endstream, whose length's object is lost", "Clean")]
    [InlineData("a header glued to a sign past an xref keyword more than 64 bytes of white space precede on its line, in a stream with no endstream", "Raw")]
    [InlineData("a header glued to a sign past an xref keyword 63 bytes of white space precede on its line, in a stream with no endstream", "Clean")]
    [InlineData("a header glued to a sign past an xref keyword that starts its line after a carriage return, in a stream with no endstream", "Clean")]
    [InlineData("a header glued to a sign past a trailer keyword that starts its line past a thousand that do not, in a stream with no endstream", "Raw")]
    [InlineData("a header in the data of a stream of type EmbeddedFile whose direct length runs past the end of the file", "Raw")]
    [InlineData("a header past an attached PDF file that lost its endstream and endobj, its length's object next, 56 bytes of white space before it", "Clean")]
    [InlineData("a header past an attached PDF file that lost its endstream and endobj, its length's object next, 57 bytes of white space before it", "Raw")]
    [InlineData("a header in the data of a stream whose length's object, before it, a trailer keyword follows", "Raw")]
    [InlineData("a header in the data of a stream whose length's object, before it, a cross-reference stream follows", "Raw")]
    [InlineData("a header in the data of a stream whose length's object before it holds a length no endstream follows, another past it one that does", "Raw")]
    [InlineData("a header in the data of a stream whose length's object follows the object after it", "Raw")]
    [InlineData("a header in the data of a stream whose length's object follows a header the walk back from its obj cannot read", "Raw")]
    [InlineData("a header in the data of a stream of type EmbeddedFile whose length's object follows the object after it", null)]
    [InlineData("a header in an attached PDF file whose length's object follows the object after it", null)]
    [InlineData("a header glued to a sign in the data of a stream with no endstream whose length's object is lost, past an xref keyword a string there holds", "Raw")]
    [InlineData("a header glued to a sign in the data of a stream with no endstream whose length's object is lost, past a trailer keyword a string there holds", "Raw")]
    public void A_header_is_recorded_where_the_scan_takes_it_with_how_it_took_it(string shape, string? kind)
    {
        var text = shape switch
        {
            // Object 6, the length of stream 5's data, comes before a trailer keyword, or before a cross-reference stream:
            // it is an older revision's, which may hold the length of a stream the next revision rewrote, and the scan reads
            // no length from it (#363). No header past the data holds the length: the scan reads the data as the bytes give
            // it.
            "a header in the data of a stream whose length's object, before it, a trailer keyword follows" =>
                "6 0 obj\n22\nendobj\ntrailer\n<< /Size 3 >>\n5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n",
            "a header in the data of a stream whose length's object, before it, a cross-reference stream follows" =>
                "6 0 obj\n22\nendobj\n7 0 obj\n<< /Type /XRef /Size 3 /W [1 1 1] /Length 3 >>\nstream\nabc\nendstream\nendobj\n" +
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n",

            // Object 6, right before stream 5, holds a length no endstream follows: it decides, and the scan searches past
            // the data for no other object 6, whose length an endstream follows (#363).
            "a header in the data of a stream whose length's object before it holds a length no endstream follows, another past it one that does" =>
                "6 0 obj\n21\nendobj\n5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n6 0 obj\n22\nendobj\n",

            // Object 6, which holds the length of stream 5's data, comes past object 4, the first header past the data: the
            // scan tries that first header alone, in data that is no attached file's (#363), or one whose walk back from its
            // obj runs past where the search's read starts, which it cannot read; an attached file's data holds objects of its
            // own, and the scan reads past object 4 to object 6.
            "a header in the data of a stream whose length's object follows the object after it" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n4 0 obj\n(four)\nendobj\n6 0 obj\n22\nendobj\n",
            "a header in the data of a stream whose length's object follows a header the walk back from its obj cannot read" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n" + new string(' ', 100) + "4\n" +
                string.Concat(Enumerable.Repeat("% a comment between the tokens of a header\n", 3)) + "0\nobj\n(four)\nendobj\n6 0 obj\n22\nendobj\n",
            "a header in the data of a stream of type EmbeddedFile whose length's object follows the object after it" =>
                "5 0 obj\n<< /Type /EmbeddedFile /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n4 0 obj\n(four)\nendobj\n6 0 obj\n22\nendobj\n",
            "a header in an attached PDF file whose length's object follows the object after it" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n%PDF-1.4\n9 0 obj (inner) endobj\nendstream\nendobj\n4 0 obj\n(four)\nendobj\n6 0 obj\n31\nendobj\n",

            // Object 8's value, 40, ends the data past its first header, and the word stream in it keeps the data from being
            // held as intact: the memo counts what the scan takes past that end as taken between objects, the scan reading
            // the data as the bytes give it up to its first endstream, and reading on from there. A comment or a string past
            // that end holds the data's first endstream, and hides what follows it on its line: the memo holds object 9's
            // header, which the scan takes there, as taken in the data. So it does past a direct length no endstream
            // follows, and no further than the line's end.
            "a header past the first endstream of data a length an earlier object of its number gives cuts, its last line a comment" =>
                "8 0 obj\n40\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n4 0 obj (four) endobj\na word stream in plain text %endstream endobj 9 0 obj (nine) endobj\n",
            "a header past the first endstream of data a length an earlier object of its number gives cuts, its last line a string" =>
                "8 0 obj\n40\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n4 0 obj (four) endobj\na word stream in plain text (endstream endobj 9 0 obj (nine) endobj\n",
            "a header on the line of a comment that holds the first endstream of a stream with no endstream past its declared length" =>
                "5 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n% endstream 9 0 obj (nine) endobj\n",
            "a header on the line after a comment that holds the first endstream of a stream with no endstream past its declared length" =>
                "5 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n% endstream\n9 0 obj (nine) endobj\n",

            // So it does where object 8, the length's object, comes after that line, which the scan meets awaiting it, or never
            // comes, the word stream in the data keeping it from being held as intact. Past an end of line, nothing hides
            // object 9; past one a backslash escapes, the string runs on, as a load reads it.
            "a header on the line of a comment that holds the first endstream of a stream with no endstream, its length's object after it" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n% endstream 9 0 obj (nine) endobj\n8 0 obj\n10\nendobj\n",
            "a header on the line of a string that holds the first endstream of a stream with no endstream, its length's object after it" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n( endstream 9 0 obj (nine) endobj\n8 0 obj\n10\nendobj\n",
            "a header on the line of a comment that holds the first endstream of a stream with no endstream, its length's object lost" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n(a stream word)\n% endstream 9 0 obj (nine) endobj\n",
            "a header on the line after a comment that holds the first endstream of a stream with no endstream, its length's object after it" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n% endstream\n9 0 obj (nine) endobj\n8 0 obj\n10\nendobj\n",
            "a header past an escaped end of line, in a string that holds the first endstream of a stream with no endstream past its length" =>
                "8 0 obj\n10\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n( endstream \\\n9 0 obj (nine) endobj\n",
            "a header past an end of line, after a string that holds the first endstream of a stream with no endstream past its length" =>
                "8 0 obj\n10\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n( endstream \n9 0 obj (nine) endobj\n",

            // Stream 5 lost its endstream, and object 8, its length's object, is lost: a load ends its data at object 10's header,
            // past object 9's, whose number a sign glued to it keeps a load's search from stopping at. A string in the data
            // quotes the word xref or trailer, which a load reads in that string and which ends no data: the memo holds object
            // 9's header, which the scan takes in the data, as taken there.
            "a header glued to a sign in the data of a stream with no endstream whose length's object is lost, past an xref keyword a string there holds" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\nBT (see the xref table) Tj ET\n+9 0 obj\n(nine)\nendobj\n10 0 obj\n(ten)\nendobj\n",
            "a header glued to a sign in the data of a stream with no endstream whose length's object is lost, past a trailer keyword a string there holds" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\nBT (see the trailer here) Tj ET\n+9 0 obj\n(nine)\nendobj\n10 0 obj\n(ten)\nendobj\n",
            "a header in data a length an earlier object of its number gives cuts, a string a header there a comment holds runs over" =>
                "8 0 obj\n40\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n4 0 obj (four) endobj\n% 7 0 obj (a string that runs on\n9 0 obj (nine) on to here) a word stream\nendstream\nendobj\n",
            "a header past data a length an earlier object of its number gives cuts, where a stream a comment there holds ends" =>
                "8 0 obj\n40\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n4 0 obj (four) endobj\n% 6 0 obj << /Length 7 0 R /X (endstream) >> stream\nplain text %endstream endobj 9 0 obj (nine) endobj\n",

            // Object 8's value ends stream 5's data at its first header; the dictionary of stream 6 there spells endstream,
            // so that its data starts past that first endstream, and its direct /Length, which no endstream follows, ends
            // that data before object 9's header: the memo counts places as taken in data up to where that length ends it,
            // past where object 8's ends stream 5's data.
            "a header past where a load ends the data of a stream whose dictionary, in data a length an earlier object gives cuts, spells endstream" =>
                "8 0 obj\n40\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n4 0 obj (four) endobj\n6 0 obj << /Length 3 /X (endstream) >> stream\nabcdefgh\n9 0 obj (nine) endobj\nplain text\nendstream\nendobj\n",
            "a header between a stream with no endstream and the object a few objects on that holds its indirect length" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n7 0 obj\n(seven)\nendobj\n9 0 obj\n(nine)\nendobj\n8 0 obj\n10\nendobj\n",
            "a header past a stream with no endstream whose indirect length a value before a stream before it holds" =>
                "8 0 obj\n10\nendobj\n6 0 obj\n<< /Length 3 >>\nstream\nabc\nendstream\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n9 0 obj\n(nine)\nendobj\n",
            "a header between a stream with no endstream and a stream before the object that holds its indirect length" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n9 0 obj\n(nine)\nendobj\n6 0 obj\n<< /Length 3 >>\nstream\nabc\nendstream\nendobj\n8 0 obj\n10\nendobj\n",
            "a header before a stream whose data holds a header of the object that holds the indirect length past both" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n9 0 obj\n(nine)\nendobj\n6 0 obj\n<< /Length 4 0 R >>\nstream\n8 0 obj\n99\nendobj\nendstream\nendobj\n8 0 obj\n10\nendobj\n",
            "a header in the data of an intact stream whose first header holds an integer as its indirect length's object" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n%PDF-1.4\n6 0 obj\n5\nendobj\n9 0 obj\n(inner)\nendobj\nendstream\nendobj\n6 0 obj\n49\nendobj\n",
            "a header in the data of a stream the data of a stream with no endstream holds, its length's object past both" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n6 0 obj\n<< /Length 7 0 R >>\nstream\n9 0 obj\n(nine)\nendobj\nendstream\nendobj\n8 0 obj\n10\nendobj\n",
            "a header in what a string that runs past its object's end runs into, past a stream with no endstream" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n7 0 obj\n(a ( b\nendobj\n9 0 obj\n(nine)\nendobj\n) )\nendobj\n8 0 obj\n10\nendobj\n",
            "a header past a stream with no endstream, a stream with no endstream whose length is awaited in its turn after it" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n9 0 obj\n(nine)\nendobj\n6 0 obj\n<< /Length 3 >>\nstream\nabc\nendstream\nendobj\n"
                + "4 0 obj\n<< /Length 7 0 R >>\nstream\n0123\nendobj\n3 0 obj\n(three)\nendobj\n8 0 obj\n10\nendobj\n",
            "a header in a comment between a stream with no endstream and the object a few objects on that holds its length" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n% 9 0 obj\n7 0 obj\n(seven)\nendobj\n8 0 obj\n10\nendobj\n",
            "a header past the object whose header a stream with no endstream declares a length past" =>
                "5 0 obj\n<< /Length 40 >>\nstream\n0123456789\n8 0 obj\n(eight)\nendobj\n9 0 obj\n(nine)\nendobj\n",
            "a header past the object whose header a stream with no endstream declares an indirect length past" =>
                "7 0 obj\n40\nendobj\n5 0 obj\n<< /Length 7 0 R >>\nstream\n0123456789\n8 0 obj\n(eight)\nendobj\n9 0 obj\n(nine)\nendobj\n",
            "a header past the object that holds the indirect length its stream with no endstream declares past it" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\n8 0 obj\n40\nendobj\n9 0 obj\n(nine)\nendobj\n",
            "a header past the endobj of a stream with no endstream that declares a length past the end of the file" =>
                "5 0 obj\n<< /Length 99999 >>\nstream\n0123456789\nendobj\n9 0 obj\n(nine)\nendobj\n",
            "a header in the data of a stream with no endstream that declares a length past the end of the file" =>
                "5 0 obj\n<< /Length 99999 >>\nstream\n0123456789\n9 0 obj\n(nine)\nendobj\n",
            "a header past the endobj of a stream with no endstream whose indirect length runs past the end of the file" =>
                "8 0 obj\n99999\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n9 0 obj\n(nine)\nendobj\n",
            "a header past the endobj of a stream with no endstream whose indirect length is the largest integer" =>
                "8 0 obj\n9223372036854775807\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n9 0 obj\n(nine)\nendobj\n",
            "a header past the endobj of a stream with no endstream whose direct length is the largest integer" =>
                "5 0 obj\n<< /Length 9223372036854775807 >>\nstream\n0123456789\nendobj\n9 0 obj\n(nine)\nendobj\n",
            "a header on the line of a string the data of a stream with no endstream opens past its declared length" =>
                "5 0 obj\n<< /Length 5 >>\nstream\n01234(abc 9 0 obj\n(nine)\nendobj\n",
            "a header on the line of a comment the data of a stream with no endstream opens past its declared length" =>
                "5 0 obj\n<< /Length 5 >>\nstream\n01234%abc 9 0 obj\n(nine)\nendobj\n",
            "a header on the line of a string the data of a stream with no endstream opens past its indirect length" =>
                "8 0 obj\n5\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n01234(abc 9 0 obj\n(nine)\nendobj\n",
            "a header that starts where the declared length of a stream with no endstream ends" =>
                "5 0 obj\n<< /Length 11 >>\nstream\n0123456789\n9 0 obj\n(nine)\nendobj\n",

            // Stream 5's data holds a header, its length's object comes last, and a load with the length it holds ends the
            // data before that header. Stream 6, after it, has its own indirect length: it awaits nothing where its first
            // header lies past its endstream, and the scan goes on awaiting stream 5's length's object; nor where stream 6
            // declares a direct length that ends where that object's header starts, the scan meeting the object past where
            // it counts places as taken in data. The memo then holds the header in stream 5's data as taken between objects.
            "a header in the data of a stream whose indirect length's object follows a stream whose first header lies past its endstream" =>
                "5 0 obj\n<< /Length 7 0 R >>\nstream\n0123 9 0 obj (x) endobj\nendstream\nendobj\n6 0 obj\n<< /Length 8 0 R >>\nstream\nabc\nendstream\nendobj\n"
                + "4 0 obj (four) endobj\n7 0 obj\n3\nendobj\n",
            "a header in the data of a stream whose indirect length's object starts where a stream with no endstream after it declares its data ends" =>
                "5 0 obj\n<< /Length 7 0 R >>\nstream\n0123 9 0 obj (x) endobj\nendstream\nendobj\n6 0 obj\n<< /Length 11 >>\nstream\n0123456789\n7 0 obj\n3\nendobj\n",

            // Stream 6 lies in stream 5's data, which the scan reads as the bytes give it up to its first endstream, while it
            // awaits stream 5's length's object: it looks for stream 6's length's object in stream 5's place, and object 8,
            // the value before it, holds a length short of the header stream 6's data holds, which the memo then holds as
            // taken between objects.
            "a header in the data of a stream whose indirect length a value in the data of a stream the scan does not confirm holds" =>
                "5 0 obj\n<< /Length 7 0 R >>\nstream\n8 0 obj\n5\nendobj\n6 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789 9 0 obj (nine) endobj\nendstream x\nendobj\n",

            // Object 8, which holds stream 5's length, lies on a comment line past the data's first endstream, where the scan
            // no longer counts places as taken in the data: the length ends the data before object 4's header, which the memo
            // then holds as taken between objects, and leaves that count where it stands. Object 9's header starts before
            // that endstream, its tokens parted by the comment lines of that endstream and of object 8, and the scan takes it
            // after object 8's: as taken in the data, where it lies.
            "a header in the data of a stream that a walk back over comment lines reads after its length's object, past the data's first endstream" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\n4 0 obj (four) endobj\n+9\n% endstream\n% 8 0 obj 10 endobj\n0 obj\n(nine)\nendobj\n",

            // Stream 5's direct length, which no endstream follows, ends its data where the data of stream 6, whose header it
            // holds, starts: the sign before 6 keeps a load's search for endstream from taking it for a header, so that the
            // length ends the data there. No place past that start is counted as taken in data, so the scan reads stream 6's
            // length's object at the first header past it, and holds that header, past where that length ends stream 6's
            // data, as taken between objects.
            "a header that holds the indirect length of a stream whose data starts where the direct length of the stream holding it ends" =>
                "5 0 obj\n<< /Length 36 >>\nstream\n+6 0 obj << /Length 9 0 R >> stream\n0123456789\n9 0 obj\n5\nendobj\nendstream x\nendobj\n",

            // Past where a load ends the data of a stream that lost its endstream, the memo holds a place as taken between objects
            // where a scan between objects would take it: past an object it takes there, as that scan passes one, a string
            // of it that runs over its line included; where the scan learns that end only past the place, the bytes are swept
            // from there, no object passed, and such a string, ended at its line's end, leaves a comment the line after holds.
            "a header past an object whose string holds a percent sign, past the endobj that follows the declared length of a stream with no endstream" =>
                "5 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n7 0 obj\n(50% off)\nendobj\n9 0 obj\n(nine)\nendobj\n",
            "a header on the line an object whose string runs over a line ends, past the endobj that follows the declared length of a stream with no endstream" =>
                "5 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n7 0 obj\n(a\n%) endobj 9 0 obj\n(nine)\nendobj\n",
            "a header on the line an object whose string runs over a line ends, past the endobj of a stream with no endstream whose length's object follows" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n7 0 obj\n(a\n%) endobj 9 0 obj\n(nine)\nendobj\n8 0 obj\n10\nendobj\n",

            // Object 6, the first in stream 5's data, holds an integer, and the file has no other object 6. In plain data, it
            // gives the stream's length, which the stream keyword after it shows no endstream follows, and the memo holds object
            // 9's header past it as taken between objects. Data that starts with a file's header, after white space too, or
            // whose dictionary gives /Type /EmbeddedFile, is an attached file's, whose objects are its own: the scan reads no
            // length's object inside it, awaits one past it, never meets it, and holds what it takes in it as taken there.
            "a header in the data of a stream that holds an object of its length's number, which the file lacks" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\nNotes.\n6 0 obj\n5\nendobj\n9 0 obj\n(inner)\nendobj\n" + InnerStream + "endstream\nendobj\n",
            "a header in an attached PDF file that holds an object of its stream's length's number, which the file lacks" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n%PDF-1.4\n6 0 obj\n5\nendobj\n9 0 obj\n(inner)\nendobj\n" + InnerStream + "%%EOF\nendstream\nendobj\n",
            "a header in an attached PDF file after a blank line, that holds an object of its stream's length's number, which the file lacks" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n\n%PDF-1.4\n6 0 obj\n5\nendobj\n9 0 obj\n(inner)\nendobj\n" + InnerStream + "%%EOF\nendstream\nendobj\n",
            "a header in the data of a stream of type EmbeddedFile that holds an object of its length's number, which the file lacks" =>
                "5 0 obj\n<< /Type /EmbeddedFile /Length 6 0 R >>\nstream\nNotes.\n6 0 obj\n5\nendobj\n9 0 obj\n(inner)\nendobj\n" + InnerStream + "endstream\nendobj\n",

            // Stream 5's length's object comes more than eight values before it, so that the scan never meets it: an attached
            // FDF file with a stream of its own keeps its places held as taken in it, and so does data a mebibyte long, an
            // attached file's or not, past which the scan does not tell whether a stream keyword lies before its endstream.
            "a header in an attached FDF file with a stream, its stream's length's object more than eight values before it" => Measured(
                "6 0 obj\nLENGTH\nendobj\n" + TenValues + "5 0 obj\n<< /Length 6 0 R >>\nstream\n[[%FDF-1.2\n1 0 obj << /A 1 >> endobj\n9 0 obj\n(inner)\nendobj\n"
                + InnerStream + "trailer << /Root 1 0 R >>\n%%EOF\n]]endstream\nendobj\n"),
            "a header in a stream's data longer than a mebibyte, its length's object more than eight values before it" => Measured(
                "6 0 obj\nLENGTH\nendobj\n" + TenValues + "5 0 obj\n<< /Length 6 0 R >>\nstream\n[[9 0 obj << /Inner true >> endobj\n"
                + string.Concat(Enumerable.Repeat(new string('a', 57) + "\n", 18_200)) + "]]endstream\nendobj\n"),
            "a header in an attached file's data longer than a mebibyte, its length's object more than eight values before it" => Measured(
                "6 0 obj\nLENGTH\nendobj\n" + TenValues + "5 0 obj\n<< /Type /EmbeddedFile /Length 6 0 R >>\nstream\n[[9 0 obj << /Inner true >> endobj\n"
                + string.Concat(Enumerable.Repeat(new string('a', 57) + "\n", 18_200)) + "]]endstream\nendobj\n"),

            // An attached PDF file holds its own object 6, which holds 3, past its first endstream: the scan takes neither that
            // object's length nor the stream's own, past the data, for its data's length, but the one past it, which an
            // endstream follows: the data is intact, and the memo holds what the scan took in it, past its first endstream
            // too, as taken there.
            "a header past the first endstream of an attached PDF file that holds an object of its stream's length's number past it" => Measured(
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n[[%PDF-1.4\n" + InnerStream + "6 0 obj\n3\nendobj\n9 0 obj\n(inner)\nendobj\n%%EOF\n]]endstream\nendobj\n"
                + "6 0 obj\nLENGTH\nendobj\n"),

            // The attached file lost its endstream, and its length, which object 6 past it holds, ends its data where endobj
            // follows: the memo holds what the scan takes past there as taken between objects.
            "a header past an attached PDF file that lost its endstream, its length's object after it" => Measured(
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n[[%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n]]endobj\n6 0 obj\nLENGTH\nendobj\n"
                + "9 0 obj\n(nine)\nendobj\n" + InnerStream),

            "a header past an attached PDF file that lost its endstream, its length's object past an intact stream after it" => Measured(
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n[[%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n]]endobj\n9 0 obj\n(nine)\nendobj\n"
                + InnerStream + "6 0 obj\nLENGTH\nendobj\n"),
            "a header past an attached PDF file that lost its endstream, its length's object before it" => Measured(
                "6 0 obj\nLENGTH\nendobj\n5 0 obj\n<< /Length 6 0 R >>\nstream\n[[%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n]]endobj\n"
                + "9 0 obj\n(nine)\nendobj\n" + InnerStream),

            // The length object 6 holds, before the stream or after it, ends the attached file's data where neither an
            // endstream nor an endobj follows, or past the end of the file: the scan does not take it, and the data's places
            // stay held as taken in it. So do they where its direct length ends it short, with no endobj there.
            "a header in an attached PDF file whose length's object before it holds a length neither an endstream nor an endobj follows" =>
                "6 0 obj\n5\nendobj\n5 0 obj\n<< /Length 6 0 R >>\nstream\n%PDF-1.4\n9 0 obj\n(inner)\nendobj\n" + InnerStream + "%%EOF\nendstream\nendobj\n",
            "a header in an attached PDF file whose length's object after it holds a length past the end of the file" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n%PDF-1.4\n9 0 obj\n(inner)\nendobj\n" + InnerStream + "%%EOF\nendstream\nendobj\n6 0 obj\n99999\nendobj\n",
            "a header past an attached PDF file whose direct length an endobj follows, its endstream lost" => Measured(
                "5 0 obj\n<< /Type /EmbeddedFile /Length LENGTH >>\nstream\n[[%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n]]endobj\n9 0 obj\n(nine)\nendobj\n"
                + InnerStream),
            "a header in an attached PDF file whose direct length ends short of its data, no endstream following" =>
                "5 0 obj\n<< /Type /EmbeddedFile /Length 5 >>\nstream\n%PDF-1.4\n9 0 obj\n(inner)\nendobj\n" + InnerStream + "%%EOF\nendstream\nendobj\n",

            // The attached file lost its endstream, and its length, direct, or which object 6 before the stream or past it
            // holds, ends its data where endobj follows, past the data's first endstream, its own stream's: the places the
            // scan takes in the data up to there, object 9's header past that endstream among them, are the attached file's,
            // and the memo holds them as taken in it.
            "a header past the first endstream of an attached PDF file whose direct length an endobj follows, its endstream lost" => Measured(
                "5 0 obj\n<< /Type /EmbeddedFile /Length LENGTH >>\nstream\n[[%PDF-1.4\n" + InnerStream + "9 0 obj\n(inner)\nendobj\n%%EOF\n]]endobj\n10 0 obj\n(ten)\nendobj\n"),
            "a header past the first endstream of an attached PDF file whose length's object before it holds a length an endobj follows, its endstream lost" => Measured(
                "6 0 obj\nLENGTH\nendobj\n5 0 obj\n<< /Length 6 0 R >>\nstream\n[[%PDF-1.4\n" + InnerStream + "9 0 obj\n(inner)\nendobj\n%%EOF\n]]endobj\n10 0 obj\n(ten)\nendobj\n"),
            "a header past the first endstream of an attached PDF file whose length's object after it holds a length an endobj follows, its endstream lost" => Measured(
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n[[%PDF-1.4\n" + InnerStream + "9 0 obj\n(inner)\nendobj\n%%EOF\n]]endobj\n6 0 obj\nLENGTH\nendobj\n10 0 obj\n(ten)\nendobj\n"),

            // The attached file lost its endstream and its endobj: its length, direct or held by object 6 after it, ends its data
            // where object 9's header follows, after white space, which the memo holds as taken between objects.
            "a header that starts where the direct length of an attached file that lost its endstream and endobj ends" => Measured(
                "5 0 obj\n<< /Type /EmbeddedFile /Length LENGTH >>\nstream\n[[%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n]]\r\n9 0 obj\n(nine)\nendobj\n"
                + InnerStream),
            "a header that starts where the indirect length of an attached file that lost its endstream and endobj ends" => Measured(
                "5 0 obj\n<< /Type /EmbeddedFile /Length 6 0 R >>\nstream\n[[%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n]]\r\n9 0 obj\n(nine)\nendobj\n"
                + InnerStream + "6 0 obj\nLENGTH\nendobj\n"),

            // The attached file writes an object of its stream's length's number, which the scan meets awaiting object 6: its 0,
            // the file's header line read as white space before its first object, or the length of that line, ends the data
            // at that object, object 9's header. It is the file's own, and object 9 is held as taken in the data, as object 6
            // after the stream, where there is one, shows it is.
            "a header in an attached PDF file whose own object of its length's number holds 0, its length's object after it" => Measured(
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n[[%PDF-1.4\n9 0 obj (inner) endobj\n7 0 obj << /Length 6 0 R >> stream\nendstream endobj\n"
                + "6 0 obj 0 endobj\n%%EOF\n]]endstream\nendobj\n6 0 obj\nLENGTH\nendobj\n"),
            "a header in an attached PDF file whose own object of its length's number ends its data at that header, its length's object lost" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n%PDF-1.4\n9 0 obj (inner) endobj\n6 0 obj 9 endobj\n%%EOF\nendstream\nendobj\n",

            // An attached text holds no object: object 6 past object 9, the first header past its data, ends the data where an
            // endobj follows, and object 9 is held as taken between objects.
            "a header past an attached text that lost its endstream, the first header after it, its length's object past it" => Measured(
                "5 0 obj\n<< /Type /EmbeddedFile /Length 6 0 R >>\nstream\n[[<invoice>4 items</invoice>\n]]endobj\n9 0 obj (nine) endobj\n6 0 obj\nLENGTH\nendobj\n"),

            // Object 9 written three times at one distance is held as a run, its first and last places swept.
            "a header written again and again at one distance past the endobj of a stream with no endstream whose length's object follows" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n9 0 obj (n) endobj\n9 0 obj (n) endobj\n9 0 obj (n) endobj\n8 0 obj\n10\nendobj\n",

            // Stream 5 awaits object 8, which only the attached file after it holds, past its first endstream: the attached
            // file's data ends the wait, which a load with no length ends at object 9's header.
            "a header past a stream with no endstream, past which an attached PDF file holds an object of its length's number past its first endstream" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\n9 0 obj\n(nine)\nendobj\n6 0 obj\n<< /Type /EmbeddedFile /Length 13 0 R >>\nstream\n%PDF-1.4\n"
                + InnerStream + "8 0 obj\n99999\nendobj\n%%EOF\nendstream\nendobj\n",

            // A pair no reference may name is read as a direct length, as a load reads it.
            "a header past where a length written with a negative generation ends the data of a stream with no endstream" =>
                "5 0 obj\n<< /Length 10 -1 R >>\nstream\n0123456789\n9 0 obj\n(nine)\nendobj\n",
            "a header past where a length written with a generation past the largest ends the data of a stream with no endstream" =>
                "5 0 obj\n<< /Length 10 65536 R >>\nstream\n0123456789\n9 0 obj\n(nine)\nendobj\n",

            // Stream 6's header lies in stream 5's data, which the scan awaits object 7 for: it awaits object 8 in its place,
            // and ends stream 5's wait where a load with no length ends its data, at stream 6's header.
            "a header past a stream with no endstream whose header lies in the data of another, each length's object past both" =>
                "5 0 obj\n<< /Length 7 0 R >>\nstream\n0123\n6 0 obj\n<< /Length 8 0 R >>\nstream\nabcd\n9 0 obj\n(nine)\nendobj\n7 0 obj\n4\nendobj\n8 0 obj\n4\nendobj\n",

            // Stream 5's length's object is lost, and a load with no length ends its data at object 10's header, the first it
            // reads as one; an xref or a trailer keyword the scan took before it ends the data there too, as a load that reads
            // the index ends it before a section, where the next object it places lies.
            "a header glued to a sign past an xref keyword between a stream with no endstream, whose length's object is lost, and the next header" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nxref\n+9 0 obj\n(nine)\n10 0 obj\n(ten)\nendobj\n",
            "a header glued to a sign past a trailer keyword between a stream with no endstream, whose length's object is lost, and the next header" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\ntrailer\n<< /Size 3 >>\n+9 0 obj\n(nine)\n10 0 obj\n(ten)\nendobj\n",
            "a header glued to a sign past a trailer keyword that starts the data of a stream with no endstream, whose length's object is lost" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\ntrailer\n<< /Size 3 >>\n+9 0 obj\n(nine)\n10 0 obj\n(ten)\nendobj\n",

            // An attached file's direct length that runs past the end of the file ends neither its data nor its object there:
            // its places stay held as taken in the data.
            "a header in the data of a stream of type EmbeddedFile whose direct length runs past the end of the file" =>
                "5 0 obj\n<< /Type /EmbeddedFile /Length 99999 >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n",

            // An attached PDF file lost its endstream and endobj, and its length's object, which the scan awaits, comes next: its
            // header ends the data where the length does, and object 9's past it is held as taken between objects, where the 64
            // bytes read past that length hold the header whole; where they end with its obj, it is not told, and the places stay
            // held as taken in the data, the wait never ending.
            "a header past an attached PDF file that lost its endstream and endobj, its length's object next, 56 bytes of white space before it" =>
                "5 0 obj\n<< /Type /EmbeddedFile /Length 8 0 R >>\nstream\n%PDF-1.4\n4 0 obj (inner) endobj\n" + new string(' ', 56) +
                "8 0 obj\n32\nendobj\n9 0 obj\n(nine)\nendobj\n",
            "a header past an attached PDF file that lost its endstream and endobj, its length's object next, 57 bytes of white space before it" =>
                "5 0 obj\n<< /Type /EmbeddedFile /Length 8 0 R >>\nstream\n%PDF-1.4\n4 0 obj (inner) endobj\n" + new string(' ', 57) +
                "8 0 obj\n32\nendobj\n9 0 obj\n(nine)\nendobj\n",

            // White space alone lies between the keyword and the end of line before it, but farther than the 64 bytes read
            // back: the keyword is not told to start its line, and ends no data.
            "a header glued to a sign past an xref keyword more than 64 bytes of white space precede on its line, in a stream with no endstream" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\n" + new string(' ', 70) + "xref\n+9 0 obj\n(nine)\n10 0 obj\n(ten)\nendobj\n",
            "a header glued to a sign past an xref keyword 63 bytes of white space precede on its line, in a stream with no endstream" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\n" + new string(' ', 63) + "xref\n+9 0 obj\n(nine)\n10 0 obj\n(ten)\nendobj\n",
            "a header glued to a sign past an xref keyword that starts its line after a carriage return, in a stream with no endstream" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\rxref\n+9 0 obj\n(nine)\n10 0 obj\n(ten)\nendobj\n",

            // Each trailer keyword the scan took in the data, from its start, is read back for whether it starts its line, with
            // the reads made for the memo's kinds: a thousand that do not spend them, and the one that does, past them, is not
            // read, and ends no data.
            "a header glued to a sign past a trailer keyword that starts its line past a thousand that do not, in a stream with no endstream" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\n" + string.Concat(Enumerable.Repeat("a trailer ", 1000)) + "\ntrailer\n+9 0 obj\n(nine)\n10 0 obj\n(ten)\nendobj\n",
            // Stream 5 has no length, and the scan reads its data as the bytes give it up to its first endstream, or to the end
            // of the file where none follows: neither a string in it that runs past its object's end, nor the direct length
            // of a stream in it that no endstream follows, ends that data where it ends, before object 9's header.
            "a header past what a string that runs past its object's end runs into, in the data of a stream with no length" =>
                "5 0 obj\n<< >>\nstream\n7 0 obj\n(a ( b\nendobj\n) )\nendobj\n9 0 obj\n(nine)\nendobj\n",
            "a header past where a stream with no endstream declares its data ends, in the data of a stream with no length" =>
                "5 0 obj\n<< >>\nstream\n6 0 obj\n<< /Length 3 >>\nstream\nabcdefgh\n9 0 obj\n(nine)\nendobj\nendstream\nendobj\n",
            "a header in a string past the endobj that follows the declared length of a stream with no endstream" =>
                "5 0 obj\n<< /Length 5 >>\nstream\n01234\nendobj (abc 9 0 obj\n(nine)\nendobj\n",
            "a header in a comment past the endobj that follows the declared length of a stream with no endstream" =>
                "5 0 obj\n<< /Length 5 >>\nstream\n01234\nendobj % 9 0 obj\n(nine)\nendobj\n",
            "a header in a string past the endobj that follows the indirect length of a stream with no endstream" =>
                "8 0 obj\n5\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n01234\nendobj (abc 9 0 obj\n(nine)\nendobj\n",
            "a header in a comment past the endobj that follows the indirect length of a stream with no endstream" =>
                "8 0 obj\n5\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n01234\nendobj % 9 0 obj\n(nine)\nendobj\n",
            "a header that holds the indirect length of the stream with no endstream before it" =>
                "5 0 obj\n<< /Length 9 0 R >>\nstream\n0123456789\nendobj\n9 0 obj\n10\nendobj\n",

            // Sixty-one objects of as many numbers before stream 5, which form no run: the memo holds sixty-four places, as many
            // as it first makes room for, when object 9's header ends the stream's data past the last of them.
            "a header that holds the indirect length of the stream with no endstream before it, sixty-one objects before the stream" =>
                string.Concat(Enumerable.Range(10, 61).Select(number => string.Create(CultureInfo.InvariantCulture, $"{number * 10} 0 obj\n({number})\nendobj\n")))
                + "5 0 obj\n<< /Length 9 0 R >>\nstream\n0123456789\nendobj\n9 0 obj\n10\nendobj\n",
            "a header that holds the indirect length of the stream before it, whose endstream and endobj are lost" =>
                "5 0 obj\n<< /Length 9 0 R >>\nstream\n0123456789\n9 0 obj\n10\nendobj\n",
            "a header that holds the indirect length of the stream before it, whose endstream and endobj are lost, an endstream in a string past it" =>
                "5 0 obj\n<< /Length 9 0 R >>\nstream\n0123456789\n9 0 obj\n10\nendobj\n6 0 obj\n(endstream)\nendobj\n",
            "a header past a stream with no endstream whose indirect length the object before the stream holds" =>
                "8 0 obj\n10\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n9 0 obj\n(nine)\nendobj\n",
            "a header past a stream with no endstream or endobj whose indirect length the object before it holds" =>
                "8 0 obj\n10\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\n9 0 obj\n(nine)\nendobj\n",

            // Object 8, the value before stream 5, holds the length of the data before object 9's header, and its endobj ends
            // 63 or 64 bytes past its header; the data's first endstream lies past object 9, and no endobj follows it. The scan
            // takes the length only where the bytes it reads of object 8 show its endobj whole, the byte past it with it, and
            // then ends the data before object 9's header; its endobj ending on the last byte read, it takes none, awaits the
            // length's object without meeting it, and keeps the header held as taken in the data up to that endstream.
            "a header past where the value before a stream ends its data, the value's endobj ending 63 bytes past its header" =>
                "8 0 obj\n10" + new string(' ', 47) + "endobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n9 0 obj\n(nine)\nendobj\nendstream x\nendobj\n",
            "a header in data the value before its stream would end before it, the value's endobj ending 64 bytes past its header" =>
                "8 0 obj\n10" + new string(' ', 48) + "endobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n9 0 obj\n(nine)\nendobj\nendstream x\nendobj\n",

            // Object 8, the value before stream 5, holds the length of the data before 50 or 100 spaces and object 9's header;
            // the data's first endstream lies past object 9, and no endobj follows it. The first bytes read past that length,
            // all white space, do not tell whether an endstream follows it: the length leaves the kinds as they are, and the
            // header stays held as taken in the data up to that endstream. Bytes that show none hold it as taken between
            // objects.
            "a header past where the value before a stream ends its data, white space a first read after that end holds between" =>
                "8 0 obj\n10\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789" + new string(' ', 50) + "\n9 0 obj (nine) endobj\nendstream x\nendobj\n",
            "a header in data the value before its stream would end before it, white space longer than a first read after that end between" =>
                "8 0 obj\n10\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789" + new string(' ', 100) + "\n9 0 obj (nine) endobj\nendstream x\nendobj\n",
            "a header past a stream with no endstream whose indirect length names an object the file lacks" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n9 0 obj\n10\nendobj\n",
            "a header past a stream with no endstream or endobj whose indirect length names an object the file lacks" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\n9 0 obj\n(nine)\nendobj\n",
            "a header in the data of an intact stream whose indirect length names an object the file lacks" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123 9 0 obj (x) endobj\nendstream\nendobj\n",

            // Stream 5's indirect length names a pair no reference may name, which a load reads as a direct length, here one no
            // stream can have or one past the end of the file: the scan has no object to look for, reads the data as the bytes
            // give it up to its first endstream, here the end of the file, and holds object 9's header as taken there.
            "a header in the data of a stream with no endstream whose indirect length names a negative object number" =>
                "5 0 obj\n<< /Length -8 0 R >>\nstream\n0123456789\n9 0 obj\n(nine)\nendobj\n",
            "a header in the data of a stream with no endstream whose indirect length names an object number past the largest" =>
                "5 0 obj\n<< /Length 2147483648 0 R >>\nstream\n0123456789\n9 0 obj\n(nine)\nendobj\n",
            "a header in the data of a stream with no endstream whose indirect length names a negative generation" =>
                "5 0 obj\n<< /Length 99999 -1 R >>\nstream\n0123456789\n9 0 obj\n(nine)\nendobj\n",
            "a header in the data of a stream with no endstream whose indirect length names a generation past the largest" =>
                "5 0 obj\n<< /Length 99999 65536 R >>\nstream\n0123456789\n9 0 obj\n(nine)\nendobj\n",
            "a header in the data of an intact stream that the length an object of its length's number holds runs past" =>
                "5 0 obj\n<< /Length 8 1 R >>\nstream\n0123 9 0 obj (x) endobj\nendstream\nendobj\n8 0 obj\n10\nendobj\n",
            "a header past a stream with no endstream or endobj whose length names another generation than the object past it" =>
                "5 0 obj\n<< /Length 8 1 R >>\nstream\n0123456789\n9 0 obj\n(nine)\nendobj\n8 0 obj\n40\nendobj\n",
            "a header in the data of an intact stream whose length names an object that holds a real number" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123 9 0 obj (x) endobj\nendstream\nendobj\n8 0 obj\n10.0\nendobj\n",
            "a header in the data of an intact stream whose length names an object that holds a negative number" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123 9 0 obj (x) endobj\nendstream\nendobj\n8 0 obj\n-10\nendobj\n",
            "a header past a stream with no endstream whose length's object is where a stream after it resolves its own" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n9 0 obj\n(endstream)\nendobj\n6 0 obj\n4\nendobj\n"
                + "7 0 obj\n<< /Length 6 0 R >>\nstream\n0123\nendobj\n8 0 obj\n10\nendobj\n",
            // The attached file's own object 6, past its first endstream, is the first header of the length's number the search
            // for it meets: the length it holds, which no endstream follows, stops the search, and the data is read as the bytes
            // give it, as before the search, object 6 written after the stream left unread (#363).
            "a header in an attached PDF file that holds an object of its stream's length's number past its first endstream" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n%PDF-1.4\n9 0 obj\n(inner)\nendobj\n7 0 obj\n<< /Length 3 >>\nstream\nabc\nendstream\nendobj\n"
                + "6 0 obj\n3\nendobj\n%%EOF\nendstream\nendobj\n6 0 obj\n106\nendobj\n",
            "a header past the endobj of a stream with no endstream whose indirect length runs past the header" =>
                "8 0 obj\n40\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n9 0 obj\n(nine)\nendobj\n",
            "a header that a stream with no endstream declares an indirect length past" =>
                "8 0 obj\n40\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\n9 0 obj\n(nine)\nendobj\n",
            "a header after an endobj in the data of a stream whose indirect length the object before it holds" =>
                "8 0 obj\n22\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\nq endobj\n9 0 obj (x) Q\nendstream\nendobj\n",
            "a header in a file the data of a stream holds whose indirect length the object before it holds" =>
                "8 0 obj\n37\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n%PDF-1.4\n9 0 obj\n(inner)\nendobj\n%%EOF\nendstream\nendobj\n",
            "a header past the endobj that follows the declared length of a stream with no endstream" =>
                "5 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n9 0 obj\n(nine)\nendobj\n",
            "a header that follows the declared length of a stream with no endstream" =>
                "5 0 obj\n<< /Length 10 >>\nstream\n0123456789\n9 0 obj\n(nine)\nendobj\n",

            // Twenty stream headers of object 6, their direct lengths ending in one run of 12,000 spaces that a letter and an
            // endstream follow, spend what the scan's reads past declared lengths may read, four times the file, before
            // stream 5: the scan then reads stream 5's data, which no endstream follows, as an intact stream's, up to the
            // end of the file, and takes nothing there (#361).
            "a header that follows the declared length of a stream with no endstream, once the scan has read what it may past lengths" =>
                string.Concat(Enumerable.Repeat("6 0 obj<</Length 0000000720>>stream\n", 20)) + new string(' ', 12_000) + "x\nendstream\nendobj\n"
                + "5 0 obj\n<< /Length 10 >>\nstream\n0123456789\n9 0 obj\n(nine)\nendobj\n",
            "a header past the endobj that ends a stream with no endstream whose declared length runs into it" =>
                "5 0 obj\n<< /Length 13 >>\nstream\n0123456789\nendobj\n9 0 obj\n(nine)\nendobj\n",
            "a header that a stream with no endstream declares a length past" =>
                "5 0 obj\n<< /Length 40 >>\nstream\n0123456789\n9 0 obj\n(nine)\nendobj\n",
            "a header past the endobj of a stream with no endstream that declares a length past the header" =>
                "5 0 obj\n<< /Length 20 >>\nstream\n0123456789\nendobj\n9 0 obj\n(nine)\nendobj\n",
            "a header between objects" => "9 0 obj\n(nine)\nendobj\n",
            "a header whose number is written with a sign" => "+9 0 obj\n(nine)\nendobj\n",
            "a header in a comment between objects" => "% 9 0 obj\n",
            "a header in a string between objects" => "(x 9 0 obj)\n",
            "a header in the data of a stream whose length an endstream confirms" =>
                "5 0 obj\n<< /Length 22 >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n",
            "a header in the data of a stream whose length is an object" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n6 0 obj\n22\nendobj\n",

            // Object 6 written with each token on a line of its own, as a writer does: the scan reads its header as it reads
            // any, confirms the stream with the length it holds, and takes nothing the data holds (#363).
            "a header in the data of a stream whose length's object has each token on a line of its own" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n6\n0\nobj\n22\nendobj\n",
            "a header in the data of a stream whose length is an object no endstream follows the length of" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n6 0 obj\n21\nendobj\n",
            // Stream 7 lost its endstream and its length's object, 8, in the data of attached file 5, whose length's object, 6, is
            // lost too: the data of an attached file holds objects of its own, and the scan awaits no length's object of a stream
            // there, whose places stay held as taken in the attached file's data.
            "a header in the data of a stream with no endstream whose length's object is lost, in an attached file's data whose length's object is lost" =>
                "5 0 obj\n<< /Type /EmbeddedFile /Length 6 0 R >>\nstream\n7 0 obj\n<< /Length 8 0 R >>\nstream\n9 0 obj\n(nine)\nendobj\n",

            // Stream 5 lost its endstream; its length's object, 8, which the scan meets in its data awaiting it, holds a length past
            // which a load ends the data at object 9's header, the first it reads as one: past where the data of stream 6 starts,
            // in what the memo counts as taken in stream 5's data. The scan awaits no length's object for stream 6, whose own is
            // lost, and object 9's header stays held as taken in data.
            "a header in the data of a stream with no endstream, in that of a stream whose length's object, in its data, ends it past that stream's start" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n+4 0 obj (four) endobj\n+8 0 obj\n80\nendobj\n+6 0 obj\n<< /Length 10 0 R >>\nstream\n+7 0 obj (seven)\n9 0 obj\n(nine)\nendobj\n",

            // Attached file 5 lost its endstream, and its length's object, 6, is lost; its own object 6, past its first endstream,
            // holds a length that ends the data right before an endobj: an object within the data its length gives is the
            // attached file's own, and the memo keeps the places past that first endstream as they are (#366).
            "a header past an attached PDF file's object of its length's number, past the file's first endstream and within the data that length gives" =>
                "5 0 obj\n<< /Type /EmbeddedFile /Length 6 0 R >>\nstream\n%PDF-1.4\n" + InnerStream + "6 0 obj\n90\nendobj\n9 0 obj (x) endobj\n",

            // Object 6, after attached file 5, holds a length that ends its data where object 9's header starts, in the data of
            // stream 7, whose header the attached file's data holds: the places between where that length ends the data and
            // where stream 7's data starts, none, are held as taken between objects, and object 9's header stays held as taken in
            // stream 7's data.
            "a header in the data of a stream whose header an attached file's data holds, the attached file's length's object after both ending it there" =>
                "5 0 obj\n<< /Type /EmbeddedFile /Length 6 0 R >>\nstream\nabc\n7 0 obj\n<< /Length 8 0 R >>\nstream\n9 0 obj\n(nine)\nendobj\nendstream\nendobj\n6 0 obj\n39\nendobj\n",

            // A reference, not an object's header, follows where the direct length of attached file 5 ends its data: the length
            // shows no end of the stream, and the places past it stay held as taken in the attached file's data.
            "a header past an attached PDF file that lost its endstream and endobj, its direct length ending where a reference follows" => Measured(
                "5 0 obj\n<< /Type /EmbeddedFile /Length LENGTH >>\nstream\n[[%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n]]\r\n12 0 R\n9 0 obj\n(nine)\nendobj\n"
                + InnerStream),

            // Past the line of stream 5's first endstream, a string of object 7 runs over a line, and a comment on the line it
            // ends would hold object 9's header: the scan passes object 7, and takes the header between objects, past the line,
            // whatever a sweep from where a load ends the data would read there. So it does where the scan awaits the length's
            // object of stream 5 past that line, and learns where a load ends the data only past object 9.
            "a header on the line a string that runs over a line ends, past the line of a comment that holds the first endstream of a stream with no endstream" =>
                "5 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n% endstream 7 0 obj (a\n%) endobj 8 0 obj (eight) endobj 9 0 obj (nine) endobj\n",
            "a header on the line a string that runs over two lines ends, past the line of a comment that holds the first endstream of a stream with no endstream" =>
                "5 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n% endstream 7 0 obj (a \\\n\n%) endobj 9 0 obj (nine) endobj\n",
            "a header on the line a string that runs over a line ends, past the line of the first endstream of a stream with no endstream whose length's object follows" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\nendstream\n7 0 obj\n(a\n%) endobj 9 0 obj\n(nine)\nendobj\n8 0 obj\n10\nendobj\n",

            // Two xref keywords that start their lines end stream 5's data for the memo's kinds at the first: object 9's header,
            // between them, is held as taken between objects.
            "a header past two xref keywords that start their lines, between a stream with no endstream, whose length's object is lost, and the next header" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nxref\n+9 0 obj\n(nine)\nxref\n+7 0 obj\n(seven)\n10 0 obj\n(ten)\nendobj\n",

            // Object 6, before attached file 5, holds a length past the end of the file: it shows no end of the data, whose places
            // stay held as taken in it.
            "a header in an attached PDF file whose length's object before it holds a length past the end of the file, its endstream there" =>
                "6 0 obj\n99999\nendobj\n5 0 obj\n<< /Type /EmbeddedFile /Length 6 0 R >>\nstream\n%PDF-1.4\n9 0 obj\n(inner)\nendobj\n%%EOF\nendstream\nendobj\n",

            // Object 6, right after attached file 5, holds the length of its data, which ends where that object's header starts,
            // as a load ends the data at the next header: the places past are held as taken between objects (#366).
            "a header past an attached PDF file that lost its endstream and endobj, whose length's object after it ends its data where that object starts" =>
                "5 0 obj\n<< /Type /EmbeddedFile /Length 6 0 R >>\nstream\n%PDF-1.4\n1 0 obj << >> endobj\n%%EOF\n" + new string(' ', 37) + "6 0 obj\n73\nendobj\n9 0 obj (nine) endobj\n",

            // A word, not an object's number, starts the header-like tokens where the direct length of attached file 5 ends its
            // data: the length shows no end of the stream, and the places past it stay held as taken in the attached file's data.
            "a header past an attached PDF file that lost its endstream and endobj, its direct length ending where a word and a number follow" => Measured(
                "5 0 obj\n<< /Type /EmbeddedFile /Length LENGTH >>\nstream\n[[%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n]]\r\nx 0 obj\n9 0 obj\n(nine)\nendobj\n"
                + InnerStream),

            // As the case of object 9 on the line of a string that runs over a line, past the line of a comment that holds the
            // first endstream of stream 5: the end of that line lies 65,537 bytes past object 7's header, where the bytes the memo
            // reads for whether object 8's header lies on that line, in reads of 64 KiB and one byte each repeating the last byte of
            // the one before, cross from one read to the next.
            "a header on the line a string that runs over a line ends, its first byte a mebibyte's sixteenth and a byte past the object that opens it" =>
                "5 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n% endstream 7 0 obj (a" + new string('x', 65_527) +
                "\n%) endobj 8 0 obj (eight) endobj 9 0 obj (nine) endobj\n",

            // Attached file 5 lost its endstream; object 9, its length's object, lies on the line of a comment that holds the
            // data's first endstream, which the scan reads from that endstream on: the length it holds ends the data before an
            // endobj, short of that line, and the scan, which met object 9 past where the memo counts places as taken in the
            // data, holds its header as it took it, between objects (#366).
            "a header on the line of a comment that holds the first endstream of an attached file that lost its endstream, the header of its length's object" =>
                "5 0 obj\n<< /Type /EmbeddedFile /Length 9 0 R >>\nstream\n4 0 obj (four) endobj\nendobj\n% endstream 9 0 obj\n22\nendobj\n",

            // Stream 5 lost its endstream and its length's object; stream 6, with no length, in its data, lost its endstream too,
            // whose first endstream ends the line of object 3: the places past the data's start of stream 6 are its own, and
            // object 9's header stays held as taken in its data, whatever the wait for stream 5's length's object ends with.
            "a header in the data of a stream with no length, on the line of its first endstream, in the data of a stream whose length's object is lost" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n4 0 obj (four) endobj\n6 0 obj\n<< >>\nstream\n9 0 obj (nine) endobj\nendstream 3 0 obj (three) endobj\n",

            // Stream 5's length's object is lost, and an xref keyword that starts its line in its data would end it for the memo's
            // kinds; but the data of stream 6, whose header, glued to a sign, a load does not stop at, starts short of where a load
            // ends stream 5's data, at object 10's header: what the scan read as stream 5's data stops there, and the places in
            // it stay held as taken in the data.
            "a header glued to a sign past an xref keyword that starts its line, in a stream with no endstream, a stream glued to a sign past it" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nxref\n+9 0 obj\n(nine)\n+6 0 obj\n<< >>\nstream\nabcdefghijklmnop\n10 0 obj\n(ten)\nendobj\n",

            // Stream 9, with no length, in the data of stream 5, whose length's object 8 comes past both, starts its data with a
            // header of its own number: the memo holds the two headers of object 9 as one run, which ends where the data of
            // stream 9 starts. Where object 8's length ends stream 5's data, the places held as taken between objects end at that
            // start too, and the run, which reaches it, keeps its kind.
            "a header in the data of a stream with no endstream whose length's object is lost, written again where that stream's data starts" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n4 0 obj (x) endobj\n9 0 obj\n<< >>\nstream\n9 0 obj (z) endobj\nendstream\nendobj\n8 0 obj\n10\nendobj\n",

            // The data of stream 5 starts with a PDF file's header past white space: within the first 64 bytes, the data is an
            // attached file's, whose own objects lie past its first endstream, and the search for object 6 passes object 4 to
            // read it; past them, the data is another stream's, and object 4, the first header past it, decides (#363).
            "a header in an attached PDF file whose header 59 bytes of white space precede, its length's object past the object after it" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n" + new string(' ', 59) + "%PDF-1.4\n9 0 obj (inner) endobj\nendstream\nendobj\n4 0 obj\n(four)\nendobj\n6 0 obj\n90\nendobj\n",
            "a header in the data of a stream whose PDF header 60 bytes of white space precede, its length's object past the object after it" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n" + new string(' ', 60) + "%PDF-1.4\n9 0 obj (inner) endobj\nendstream\nendobj\n4 0 obj\n(four)\nendobj\n6 0 obj\n91\nendobj\n",

            // Only a /Type of EmbeddedFile says the data is an attached file's: under another key, the data is another stream's,
            // and object 4, the first header past it, decides (#363).
            "a header in the data of a stream whose subtype, not its type, is EmbeddedFile, its length's object past the object after it" =>
                "5 0 obj\n<< /Subtype /EmbeddedFile /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n4 0 obj\n(four)\nendobj\n6 0 obj\n22\nendobj\n",

            // Object 0, the head of the free list, is no object the scan takes: its header, written 32 bytes past the data's
            // first endstream and read where the search's second read starts, past an obj that starts no header, gives stream
            // 5 no length (#363).
            "a header in the data of a stream whose length names object 0, which a header past the data's first endstream holds" =>
                "5 0 obj\n<< /Length 0 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n" + new string(' ', 15) + "0 0 obj\n22\nendobj\n%" + new string('-', 11) + "xobj\n",

            // Data that starts with a PDF file's header is an attached file's, whatever its dictionary says: its direct length,
            // which an endobj follows, ends it there, and the places within, past its first endstream too, are its own (#366).
            "a header past the first endstream of an attached PDF file with no type whose direct length an endobj follows, its endstream lost" => Measured(
                "5 0 obj\n<< /Length LENGTH >>\nstream\n[[%PDF-1.4\n" + InnerStream + "9 0 obj\n(inner)\nendobj\n%%EOF\n]]endobj\n10 0 obj\n(ten)\nendobj\n"),

            // Stream 5 lost its endstream and its length's object, 8: the scan awaits it in stream 5's data, up to where the data
            // of stream 6 starts, whose direct length runs past the end of the file. Stream 7's data starts in stream 6's, past
            // where the wait stops: the scan awaits no length's object for it, and the memo counts what follows as taken in data.
            "a header in the data of a stream with no endstream, in that of a stream whose length runs past the end of the file, in that of a stream whose length's object is lost" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n6 0 obj\n<< /Length 7000 >>\nstream\n7 0 obj\n<< /Length 10 0 R >>\nstream\n9 0 obj\n(nine)\nendobj\n",

            // Past where a load ends stream 5's data, short of its first endstream, the sweep for the memo's kinds tells each
            // place. An object whose header a comment holds is not passed by that sweep, which tells the header after it, on the
            // comment's line, as in the comment; one whose header it took between objects, a stream whose data opens a string, is
            // passed, and the header after it, on the line of that stream's endstream, the data's first, is held as taken between
            // objects.
            "a header on a comment line that holds an object before it, past where a load ends the data of a stream with no endstream" =>
                "5 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n% 7 0 obj (seven) endobj 9 0 obj (nine) endobj\nendstream\nendobj\n",
            "a header on the line of the endstream of a stream whose data opens a string, past where a load ends the data of a stream with no endstream" =>
                "5 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n7 0 obj\n<< /Length 4 >>\nstream\na (b endstream endobj 9 0 obj\n(nine)\nendobj\n",

            // Stream 5's first endstream ends its line past object 8's header; stream 6's, in a comment, holds object 9's header
            // on its line: the line is searched anew for each stream, and the header held as taken in the data.
            "a header on the line of a comment that holds the first endstream of a stream with no endstream, past such a stream whose first endstream's line ends before it" =>
                "5 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\nendstream 8 0 obj (eight) endobj\n"
                + "6 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n3 0 obj (three) endobj\n% endstream 9 0 obj (nine) endobj\n",

            // The search for object 6 reads a mebibyte past the data's first endstream, where it starts: it reads a header
            // whose obj ends there, and none past (#363).
            "a header in the data of a stream whose length's object's obj ends a mebibyte past the data's first endstream" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n" + CommentLines((1024 * 1024) - 24) + "6 0 obj\n22\nendobj\n",
            "a header in the data of a stream whose length's object's obj ends a mebibyte and a byte past the data's first endstream" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n" + CommentLines((1024 * 1024) - 23) + "6 0 obj\n22\nendobj\n",

            // The search reads the 64 bytes from the data's first endstream, then from 32 bytes past it, each read repeating
            // the last 32 bytes of the one before: object 6's obj, which the first read cuts, is read back from the second,
            // whose first byte, a new line, shows that the header's number starts a token there. A number at that read's first
            // byte, whose start it cannot tell, is no header, and leaves the stream unconfirmed (#363).
            "a header in the data of a stream whose length's object's number starts 33 bytes past the data's first endstream, its obj 62" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n" + new string(' ', 15) + "\n6" + new string(' ', 26) + "0 obj\n22\nendobj\n",
            "a header in the data of a stream whose length's object's number starts 32 bytes past the data's first endstream, its obj 62" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n" + new string(' ', 14) + "\n6" + new string(' ', 27) + "0 obj\n22\nendobj\n",
            "a header in the data of a stream whose length's object lies more than a mebibyte past its endstream" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n" +
                string.Concat(Enumerable.Repeat("% " + new string('-', 1021) + "\n", 1025)) + "6 0 obj\n22\nendobj\n",

            // Object 6 holds the length of stream 5's data, which an endstream follows: the scan confirms the stream, and takes
            // nothing its data holds (#363).
            "a header after another in the data of an intact stream whose indirect length's object follows it" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n4 0 obj (four) endobj\n9 0 obj (inner) endobj\nendstream\nendobj\n6 0 obj\n44\nendobj\n",
            _ => "5 0 obj\n(a ( b\nendobj\n9 0 obj\n(nine)\nendobj\n) )\nendobj\n",
        };
        var place = text.Contains("+9", StringComparison.Ordinal) ? text.IndexOf("+9", StringComparison.Ordinal) : text.IndexOf("9 0 obj", StringComparison.Ordinal);
        var (file, at) = Sound(text, place);

        using var document = PdfDocument.Open(file);
        var memo = document.Reader.GetScanMemo();

        document.WasRepaired.Should().BeFalse();
        memo.IsWhole.Should().BeTrue();
        memo.InScanOrder.Should().BeTrue();

        if (kind is null)
        {
            memo.TryGetHeaderAt(at, out _).Should().BeFalse();
            Headers(memo, 0, long.MaxValue).Should().NotContain(header => header.Number == 9);
        }
        else
        {
            memo.TryGetHeaderAt(at, out var header).Should().BeTrue();
            header.Should().Be(new ScannedHeader(at, 9, 0, Enum.Parse<ScannedPlaceKind>(kind)));
            memo.TryGetHeaderAt(at + 1, out _).Should().BeFalse();
        }
    }

    [Theory(Timeout = 60_000)]
    [InlineData("a stream whose length's object lies past the endobj after its data")]
    [InlineData("a stream whose length's object lies past an end-of-file marker an endstream follows")]
    [InlineData("a stream with no endstream whose length's object is lost, an xref keyword in its data and the next header in the middle of their lines")]
    [InlineData("a stream with no endstream whose first endstream a string holds, an escaped end of line past it")]
    [InlineData("a stream with no endstream whose length's object, past the places in its data, ends it before them")]
    public async Task The_scan_for_the_memo_ends_over_each_shape_its_searches_and_sweeps_loop_over(string shape)
    {
        // Each shape leads the scan into a loop over the file's bytes or the memo's places: the search for a length's object
        // past an obj that starts no header, or past an end-of-file marker; the search for an xref keyword that starts its
        // line, past one that does not, and up to a header that does not; the search for the end of a line past an escaped
        // end of line; the places held as taken between objects past where a load ends the data. Each ends.
        var text = shape switch
        {
            "a stream whose length's object lies past the endobj after its data" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n6 0 obj\n22\nendobj\n",
            "a stream whose length's object lies past an end-of-file marker an endstream follows" =>
                "5 0 obj\n<< /Type /EmbeddedFile /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n%%EOF\nendstream\n6 0 obj\n22\nendobj\n",
            "a stream with no endstream whose length's object is lost, an xref keyword in its data and the next header in the middle of their lines" =>
                "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789 xref +9 0 obj (nine) 10 0 obj (ten) endobj\n",
            "a stream with no endstream whose first endstream a string holds, an escaped end of line past it" =>
                "8 0 obj\n10\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n( endstream \\\n9 0 obj (nine) endobj\n",
            _ => "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n9 0 obj (nine) endobj\nendstream\nendobj\n8 0 obj\n10\nendobj\n",
        };
        var (file, _) = Sound(text, 0);

        using var document = PdfDocument.Open(file);
        var memo = await Task.Run(document.Reader.GetScanMemo, TestContext.Current.CancellationToken);

        memo.IsWhole.Should().BeTrue();
        Headers(memo, 0, long.MaxValue).Should().Contain(header => header.Number == 5);
    }

    [Fact]
    public void A_header_taken_after_a_length_s_object_met_where_the_memo_stops_counting_places_in_data_and_starting_before_it_is_held_as_taken_in_data()
    {
        // Stream 5's indirect length names object 7, which comes last: the scan awaits it past stream 5's data. Stream 6's
        // direct length, which no endstream follows, ends its data where object 7's header starts, on a comment line, in a
        // string of object 4 that runs past the bound on objects: the scan delimits neither object, and reads tokens on from
        // where stream 6's data starts. Object 7's length ends stream 5's data before the header there, which the memo then
        // holds as taken between objects, and leaves where it counts places as taken in data where it met object 7. Object
        // 8's header, whose tokens that comment line parts, starts in stream 6's data before there, and the scan takes it
        // after object 7's: as taken in that data.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        var inner = file.Position + "5 0 obj\n<< /Length 7 0 R >>\nstream\n0123 ".Length;
        file.Raw("5 0 obj\n<< /Length 7 0 R >>\nstream\n0123 9 0 obj (x) endobj\nendstream\nendobj\n");
        var data = file.Position + "6 0 obj\n<< /Length 17 >>\nstream\n".Length;
        file.Raw("6 0 obj\n<< /Length 17 >>\nstream\n+4 0 obj (\n8 0\n% 7 0 obj 3 endobj\nobj\n" + new string('a', 300) + "\n");
        file.Table(3, rows, "/Size 3 /Root 1 0 R");

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 256 } });
        var memo = document.Reader.GetScanMemo();

        memo.TryGetHeaderAt(inner, out var cut).Should().BeTrue();
        cut.Should().Be(new ScannedHeader(inner, 9, 0, ScannedPlaceKind.Clean));
        memo.TryGetHeaderAt(data + 17, out var length).Should().BeTrue();
        length.Should().Be(new ScannedHeader(data + 17, 7, 0, ScannedPlaceKind.Clean));
        memo.TryGetHeaderAt(data + 11, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(data + 11, 8, 0, ScannedPlaceKind.Raw));
    }

    [Theory]
    [InlineData("the first value the scan passed, eight values before the stream", "Raw")]
    [InlineData("the first value the scan passed, nine values before the stream", "Clean")]
    [InlineData("the first value the scan passed, eight values before the stream, the last of them a newer object 8 that holds 10", "Clean")]
    [InlineData("the file's first object, past the bound a load reads an object within, seven values before the stream", "Clean")]
    public void A_stream_s_indirect_length_is_read_from_the_newest_of_the_last_eight_values_the_scan_passed_that_is_its_object(string where, string kind)
    {
        // Stream 5 lost its endstream and endobj, and its indirect length names object 8, the file's first object, whose value,
        // 40, runs past object 9's header in its data: where the scan reads object 8 among the last eight values it passed,
        // the newest first, the memo holds that header as taken in the data. Nine values back, or where the scan passed it as
        // no value, it awaits object 8 past the stream, never meets it, and holds the header as taken between objects, where
        // a load with no length ends the data; so it does where a newer object 8 among those eight holds 10, which ends the
        // data before the header. Objects 1 and 2 come last, after the first value, and short enough for the bound.
        var bounded = where.Contains("past the bound", StringComparison.Ordinal);
        var newer = where.Contains("a newer object 8", StringComparison.Ordinal);
        var others = where.Contains("nine values", StringComparison.Ordinal) ? 8 : newer ? 6 : 7;
        var file = new HandWrittenFile();

        // Spaces put object 8's endobj past a bound of 48 bytes, yet within the 64 bytes a length's object is read in: only
        // its not being a value the scan passed keeps it from giving the length.
        file.Raw("8 0 obj\n40" + new string(' ', bounded ? 34 : 0) + "\nendobj\n");

        for (var number = 10; number < 10 + others; number++)
        {
            file.Object(number, string.Create(CultureInfo.InvariantCulture, $"({number})"));
        }

        if (newer)
        {
            file.Object(8, "10");
        }

        file.Raw("5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\n");
        var at = file.Position;
        file.Raw("9 0 obj\n(nine)\nendobj\n");
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<</Type/Catalog/Pages 2 0 R>>"),
            [2] = file.Object(2, "<</Type/Pages/Kids[]/Count 0>>"),
        };
        file.Table(3, rows, "/Size 3 /Root 1 0 R");

        using var document = PdfDocument.Open(
            file.Bytes, bounded ? new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 48 } } : null);
        var memo = document.Reader.GetScanMemo();

        document.WasRepaired.Should().BeFalse();
        (memo.IsWhole, memo.InScanOrder).Should().Be((true, true));
        memo.TryGetHeaderAt(at, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(at, 9, 0, Enum.Parse<ScannedPlaceKind>(kind)));
    }

    [Theory]
    [InlineData("a keyword between objects", "Clean")]
    [InlineData("a keyword glued to an end-of-file marker", "Clean")]
    [InlineData("a keyword after a closing bracket", "Clean")]
    [InlineData("a keyword in the data of a stream whose length is an object", null)]
    [InlineData("a keyword in the data of a stream whose length's object lies more than a mebibyte past its endstream", "Raw")]
    [InlineData("a keyword in the data of a stream whose length is an object, its dictionary running past the scan's first piece", null)]
    [InlineData("a keyword in the data of a stream whose endstream follows past its declared length", "Raw")]
    [InlineData("a keyword past the endobj that follows the declared length of a stream with no endstream", "Clean")]
    [InlineData("a keyword past the object that holds the indirect length of the stream with no endstream before it", "Clean")]
    [InlineData("a keyword between a stream with no endstream and the object after it that holds its length", "Clean")]
    [InlineData("a keyword between a stream with no endstream and the object a few objects on that holds its length", "Clean")]
    [InlineData("a keyword past a stream with no endstream whose indirect length the object before it holds", "Clean")]
    [InlineData("a keyword past the object whose header a stream with no endstream declares a length past", "Clean")]
    [InlineData("a keyword past the object after a stream with no endstream that declares a length past the end of the file", "Clean")]
    [InlineData("a keyword on the line of a string the data of a stream with no endstream opens past its declared length", "Raw")]
    [InlineData("a keyword that starts where the declared length of a stream with no endstream ends", "Clean")]
    [InlineData("a keyword past a stream whose data runs past the end of a piece of the scan", "Clean")]
    [InlineData("a keyword between a stream with no endstream, whose length's object is lost, and the next header", "Clean")]
    [InlineData("a keyword in a comment", null)]
    [InlineData("a keyword in a string between objects", null)]
    [InlineData("a keyword inside an object", null)]
    [InlineData("a name", null)]
    [InlineData("a longer word", null)]
    [InlineData("the end of startxref", null)]
    public void An_xref_keyword_is_recorded_only_where_a_load_reads_it_as_a_token_the_scan_takes(string shape, string? kind)
    {
        var (text, word) = shape switch
        {
            "a keyword between objects" => ("xref\n", "xref"),
            "a keyword glued to an end-of-file marker" => ("%%EOFxref\n", "xref"),
            "a keyword after a closing bracket" => ("[1]xref\n", "xref"),
            "a keyword in the data of a stream whose length is an object" =>
                ("5 0 obj\n<< /Length 6 0 R >>\nstream\nxref\nendstream\nendobj\n6 0 obj\n5\nendobj\n", "xref"),
            "a keyword in the data of a stream whose length's object lies more than a mebibyte past its endstream" =>
                ("5 0 obj\n<< /Length 6 0 R >>\nstream\nxref\nendstream\nendobj\n%" + new string('-', 1024 * 1024) + "\n6 0 obj\n5\nendobj\n", "xref"),
            "a keyword in the data of a stream whose length is an object, its dictionary running past the scan's first piece" =>
                ("5 0 obj\n<< /Length 6 0 R /Pad (" + new string('p', 1_100_000) + ") >>\nstream\nxref\nendstream\nendobj\n6 0 obj\n5\nendobj\n", "xref"),
            "a keyword in the data of a stream whose endstream follows past its declared length" =>
                ("5 0 obj\n<< /Length 4 >>\nstream\n0123 xref\nendstream\nendobj\n", "xref"),
            "a keyword past the endobj that follows the declared length of a stream with no endstream" =>
                ("5 0 obj\n<< /Length 4 >>\nstream\n0123\nendobj\nxref\n", "xref"),
            "a keyword past the object that holds the indirect length of the stream with no endstream before it" =>
                ("5 0 obj\n<< /Length 9 0 R >>\nstream\n0123\nendobj\n9 0 obj\n4\nendobj\nxref\n", "xref"),
            "a keyword between a stream with no endstream and the object after it that holds its length" =>
                ("5 0 obj\n<< /Length 9 0 R >>\nstream\n0123\nendobj\nxref\n9 0 obj\n4\nendobj\n", "xref"),
            "a keyword between a stream with no endstream and the object a few objects on that holds its length" =>
                ("5 0 obj\n<< /Length 9 0 R >>\nstream\n0123\nendobj\nxref\n7 0 obj\n(seven)\nendobj\n9 0 obj\n4\nendobj\n", "xref"),
            "a keyword past a stream with no endstream whose indirect length the object before it holds" =>
                ("9 0 obj\n4\nendobj\n5 0 obj\n<< /Length 9 0 R >>\nstream\n0123\nendobj\nxref\n", "xref"),
            "a keyword past the object whose header a stream with no endstream declares a length past" =>
                ("5 0 obj\n<< /Length 40 >>\nstream\n0123\n8 0 obj\n(eight)\nendobj\nxref\n", "xref"),
            "a keyword past the object after a stream with no endstream that declares a length past the end of the file" =>
                ("5 0 obj\n<< /Length 99999 >>\nstream\n0123\nendobj\n9 0 obj\n(nine)\nendobj\nxref\n", "xref"),
            "a keyword on the line of a string the data of a stream with no endstream opens past its declared length" =>
                ("5 0 obj\n<< /Length 4 >>\nstream\n0123(abc xref\n", "xref"),
            "a keyword that starts where the declared length of a stream with no endstream ends" =>
                ("5 0 obj\n<< /Length 5 >>\nstream\n0123\nxref\n", "xref"),
            "a keyword between a stream with no endstream, whose length's object is lost, and the next header" =>
                ("5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nxref\n9 0 obj\n(nine)\nendobj\n", "xref"),
            "a keyword past a stream whose data runs past the end of a piece of the scan" =>
                ("5 0 obj\n<< /Length 1100000 >>\nstream\n" + new string('d', 1_100_000) + "\nendstream\nendobj\nxref\n", "xref"),
            "a keyword in a comment" => ("% xref\n", "xref"),
            "a keyword in a string between objects" => ("(see xref)\n", "xref"),
            "a keyword inside an object" => ("9 0 obj\n(an xref)\nendobj\n", "xref"),
            "a name" => ("/xref\n", "xref"),
            "a longer word" => ("xrefs\n", "xrefs"),
            _ => (string.Empty, string.Empty),
        };
        var (file, at) = Sound(text, word.Length == 0 ? 0 : text.IndexOf(word, StringComparison.Ordinal));
        var written = Encoding.Latin1.GetString(file);
        var table = written.LastIndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        at = word.Length == 0 ? written.LastIndexOf("startxref", StringComparison.Ordinal) + "start".Length : at;

        using var document = PdfDocument.Open(file);
        var memo = document.Reader.GetScanMemo();

        memo.TryGetXRefKeywordAt(table, out var tableKind).Should().BeTrue();
        tableKind.Should().Be(ScannedPlaceKind.Clean);
        memo.TryGetHeaderAt(table, out _).Should().BeFalse();

        if (kind is null)
        {
            memo.TryGetXRefKeywordAt(at, out _).Should().BeFalse();
        }
        else
        {
            memo.TryGetXRefKeywordAt(at, out var found).Should().BeTrue();
            found.Should().Be(Enum.Parse<ScannedPlaceKind>(kind));
        }
    }

    [Theory]
    [InlineData(1_048_569)]
    [InlineData(1_048_570)]
    [InlineData(1_048_571)]
    [InlineData(1_048_572)]
    [InlineData(1_048_573)]
    [InlineData(1_048_574)]
    [InlineData(1_048_575)]
    public void An_xref_keyword_is_recorded_wherever_it_starts_against_the_end_of_a_piece_of_the_scan(int at)
    {
        // The scan reads the file a mebibyte at a time, the last byte of a piece read only to tell a keyword that ends the
        // bytes searched: a table whose keyword starts within the first piece's last seven bytes, the keyword ending within
        // the bytes searched, past them or at the piece's last byte, is recorded as one anywhere else.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        file.Raw(new string(' ', at - (int)file.Position - 1) + "\n");
        var table = file.Table(3, rows, "/Size 3 /Root 1 0 R");

        using var document = PdfDocument.Open(file.Bytes);
        var memo = document.Reader.GetScanMemo();

        table.Should().Be(at);
        memo.TryGetXRefKeywordAt(at, out var kind).Should().BeTrue();
        kind.Should().Be(ScannedPlaceKind.Clean);
    }

    [Theory]
    [InlineData(1_048_568)]
    [InlineData(1_048_569)]
    [InlineData(1_048_570)]
    [InlineData(1_048_571)]
    [InlineData(1_048_572)]
    [InlineData(1_048_573)]
    [InlineData(1_048_574)]
    [InlineData(1_048_575)]
    public void A_trailer_keyword_is_taken_wherever_it_starts_against_the_end_of_a_piece_of_the_scan(int at)
    {
        // A trailer keyword that starts within the first piece's last eight bytes, ending within the bytes searched, past them
        // or at the piece's last byte, is taken as one anywhere else: the rebuild merges its dictionary, keys only it holds
        // among them, and the memo holds the same places wherever it lies.
        var file = new HandWrittenFile();
        file.Object(1, Catalog);
        file.Object(2, Pages);
        file.Object(3, "<< /Title (three) >>");
        file.Raw(new string(' ', at - (int)file.Position - 1) + "\n");
        file.Raw("trailer\n<< /Size 4 /Root 1 0 R /Info 3 0 R /ID [<01> <02>] >>\nstartxref\n99999999\n%%EOF\n");

        using var document = PdfDocument.Open(file.Bytes);
        var memo = document.Reader.GetScanMemo();

        Encoding.Latin1.GetString(file.Bytes).IndexOf("trailer", StringComparison.Ordinal).Should().Be(at);
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.TrailerRootRecovered);
        document.Trailer.GetRaw(PdfName.Info).Should().BeOfType<PdfReference>().Which.Id.Should().Be(new PdfObjectId(3));
        document.Trailer.ContainsKey(PdfName.ID).Should().BeTrue();
        memo.Count.Should().Be(4);
        memo.IsWhole.Should().BeTrue();
        memo.Trailers!.Keywords.Should().Equal(at + "trailer".Length);
    }

    [Theory]
    [InlineData("5 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n(a 4 0 obj (in a string) endobj\n7 0 obj\n(a\n%) endobj 9 0 obj\n(nine)\nendobj\n")]
    [InlineData("5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n% 4 0 obj\n9 0 obj\n(nine)\nendobj\nxref\n8 0 obj\n10\nendobj\n")]
    [InlineData("5 0 obj\n<< /Type /EmbeddedFile /Length 8 0 R >>\nstream\n%PDF-1.4\n4 0 obj (inner) endobj\nendobj\n8 0 obj\n32\nendobj\n% 6 0 obj\n9 0 obj\n(nine)\nendobj\n")]
    [InlineData("5 0 obj\n<< /Type /EmbeddedFile /Length 8 0 R >>\nstream\n%PDF-1.4\n4 0 obj (inner) endobj\n8 0 obj\n32\nendobj\n% 6 0 obj\n9 0 obj\n(nine)\nendobj\n")]
    [InlineData("5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nxref (a 4 0 obj\n+9 0 obj\n(nine)\n10 0 obj\n(ten)\nendobj\n")]
    [InlineData("5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n% endstream, then a note that runs on past what the next piece repeats 9 0 obj\n(nine)\nendobj\n8 0 obj\n10\nendobj\n")]
    [InlineData("8 0 obj\n10\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n( endstream, then a note that runs on past what the next piece repeats \\\n9 0 obj\n(nine)\nendobj\n")]
    public void The_memo_holds_what_the_scan_takes_past_where_a_load_ends_a_stream_s_data_alike_wherever_a_piece_of_the_scan_ends(string objects)
    {
        // Stream 5 lost its endstream, and the memo holds what the scan takes past where a load ends its data as taken between
        // objects but where a comment or a string between objects holds it: a direct length, the sweep from its end passing
        // the object it holds between objects; an indirect one whose object follows, the places taken before it swept from
        // that end once the object is met, on the line of the data's first endstream too; an attached file's, whose length
        // ends its data where its endobj, or that lost too, the next header follows; a lost length's object, an xref keyword ending the data before the next
        // header; a string that holds the data's first endstream running on over an escaped end of line. The objects are slid
        // across the end of the scan's first piece, so that it ends inside each of their bytes in turn: the memo holds the
        // same places alike, as when they lie well inside it (#171).
        var whole = Places(objects, 0);

        whole.Should().Contain(place => place.EndsWith("Raw", StringComparison.Ordinal)).And.Contain(place => place.EndsWith("Clean", StringComparison.Ordinal));

        for (var start = (1024 * 1024) - 40 - objects.Length; start < 1024 * 1024; start++)
        {
            Places(objects, start).Should().Equal(whole, $"the objects start at {start}");
        }

        static List<string> Places(string objects, long start)
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
            PadWithComments(file, start);
            var at = file.Position;
            file.Raw(objects);
            file.Table(3, rows, "/Size 3 /Root 1 0 R");
            using var document = PdfDocument.Open(file.Bytes);
            var memo = document.Reader.GetScanMemo();
            var places = Headers(memo, at, at + objects.Length)
                .Select(header => string.Create(CultureInfo.InvariantCulture, $"{header.Number} at {header.Position - at}, {header.Kind}"))
                .ToList();

            for (var position = at; position < at + objects.Length; position++)
            {
                if (memo.TryGetXRefKeywordAt(position, out var kind))
                {
                    places.Add(string.Create(CultureInfo.InvariantCulture, $"xref at {position - at}, {kind}"));
                }
            }

            return places;
        }
    }

    [Fact]
    public void A_header_whose_comment_between_its_tokens_holds_an_xref_keyword_is_taken_and_the_keyword_is_not()
    {
        // The keyword lies in a comment the header's walk crosses: the scan tells it apart from the header, which it takes,
        // and indexes, as it did before it looked for keywords.
        const string Text = "9 % xref\n0 obj\n(nine)\nendobj\n";
        var (file, at) = Sound(Text, 0);
        var broken = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(file).Replace("startxref\n", "startxref\n9", StringComparison.Ordinal));

        using var document = PdfDocument.Open(broken);
        var memo = document.Reader.GetScanMemo();

        document.WasRepaired.Should().BeTrue();
        memo.TryGetHeaderAt(at, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(at, 9, 0, ScannedPlaceKind.Clean));
        memo.TryGetXRefKeywordAt(at + 4, out _).Should().BeFalse();
        document.Reader.Index.TryGet(9, out var entry).Should().BeTrue();
        document.Reader.PositionOf(entry).Should().Be(at);
        document.GetObject(new PdfObjectId(9)).AsText().Should().Be("nine");
    }

    [Theory]
    [InlineData("% xref\nxref\nxref\n", "Raw")]
    [InlineData("xref\n% xref\n", "Raw")]
    [InlineData("xref\nxref\n", "Clean")]
    public void A_run_of_xref_keywords_past_where_a_load_ends_a_stream_s_data_is_held_as_taken_between_objects_only_where_its_first_and_last_are(
        string keywords, string kind)
    {
        // Stream 5 lost its endstream; object 8, its length's object, comes past its data's first endstream: the scan learns
        // where a load ends the data only past the xref keywords it took there, one after another, the same distance apart,
        // which the memo holds as one run. The sweep from that end holds them as taken between objects where it shows both the
        // run's first and its last there; a comment that holds either keeps the whole run held as taken in the data (#372).
        var text = "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n" + keywords +
            "9 0 obj (nine) endobj\nendstream\nendobj\n8 0 obj\n10\nendobj\n";
        var (file, start) = Sound(text, 0);

        using var document = PdfDocument.Open(file);
        var memo = document.Reader.GetScanMemo();
        var kinds = new List<ScannedPlaceKind>();

        for (var at = start + text.IndexOf("xref", StringComparison.Ordinal); at < start + text.IndexOf("9 0 obj", StringComparison.Ordinal); at++)
        {
            if (memo.TryGetXRefKeywordAt(at, out var found))
            {
                kinds.Add(found);
            }
        }

        kinds.Should().HaveCount(keywords.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length).And.OnlyContain(found => found == Enum.Parse<ScannedPlaceKind>(kind));
    }

    [Fact]
    public void A_header_whose_comment_between_its_tokens_holds_an_xref_keyword_past_where_a_load_ends_a_stream_s_data_is_held_as_taken_between_objects()
    {
        // Stream 5 lost its endstream: past where its direct length, which an endobj follows, ends its data, and short of its
        // first endstream, the scan reads the bytes as they come, and the sweep for the memo's kinds tells each place it
        // takes. The xref keyword in a comment between the tokens of object 9's header, which the scan takes there, is told
        // by a sweep ahead, which leaves the sweep for the header where it stood: the keyword is held as taken in the comment,
        // and the header as taken between objects (#372).
        const string Text = "5 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n9 0 % xref\nobj\n(nine)\nendobj\nendstream\nendobj\n";
        var (file, at) = Sound(Text, Text.IndexOf("9 0 %", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);
        var memo = document.Reader.GetScanMemo();

        memo.TryGetHeaderAt(at, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(at, 9, 0, ScannedPlaceKind.Clean));
        memo.TryGetXRefKeywordAt(at + "9 0 % ".Length, out var kind).Should().BeTrue();
        kind.Should().Be(ScannedPlaceKind.Raw);
    }

    [Fact]
    public void A_header_taken_second_that_starts_before_the_first_place_taken_is_put_back_before_it_and_a_rebuild_scans_again()
    {
        // The file's first object: its number, then an end-of-file marker an xref keyword is glued to, then its generation and
        // obj on the next line. Between objects, the marker ends its comment, so that the keyword is a token the scan takes
        // first, before the obj; a load lexes the marker's line as one comment, so that the header is object 9's, and starts
        // before the keyword. The memo puts the header back in its place, each found where it starts, and a rebuild scans the
        // file again.
        var file = new HandWrittenFile();
        var header = file.Position;
        file.Raw("9 %%EOFxref\n0 obj\n(nine)\nendobj\n");
        var keyword = header + "9 %%EOF".Length;
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages), [4] = 999_999 };
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        var bytes = file.Bytes;

        var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var memo = document.Reader.GetScanMemo();
        var before = source.BytesRead;
        document.GetObject(new PdfObjectId(4));

        memo.InScanOrder.Should().BeFalse();
        memo.TryGetXRefKeywordAt(keyword, out var kind).Should().BeTrue();
        kind.Should().Be(ScannedPlaceKind.Clean);
        memo.TryGetHeaderAt(header, out var nine).Should().BeTrue();
        nine.Should().Be(new ScannedHeader(header, 9, 0, ScannedPlaceKind.Clean));
        document.WasRepaired.Should().BeTrue();
        (source.BytesRead - before).Should().BeGreaterThanOrEqualTo(bytes.Length, "the rebuild scans the file again");
    }

    [Fact]
    public void Xref_keywords_a_megabyte_past_the_last_object_are_told_from_comments_with_one_read_of_the_bytes_before_their_piece()
    {
        // Past the catalog and the page tree, a megabyte of comment lines, then ten xref keywords, one a line, and the table,
        // all in the scan's second piece. The sweep ahead that tells each keyword from a comment reads the comment lines
        // before that piece once, for the first keyword, and goes on from where it stopped for the next; the scan's own sweep
        // reads them once more, for the table's trailer keyword: besides the file, the scan reads the bytes before the
        // keywords' piece twice, not again for each keyword.
        const int Piece = 1 << 20;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        PadWithComments(file, Piece + 100);
        var keywords = new List<long>();

        for (var index = 0; index < 10; index++)
        {
            keywords.Add(file.Position);
            file.Raw("xref\n");
        }

        keywords.Add(file.Table(3, rows, "/Size 3 /Root 1 0 R"));
        var bytes = file.Bytes;
        var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);

        var before = source.BytesRead;
        var memo = document.Reader.GetScanMemo();
        var read = source.BytesRead - before;

        keywords.Where(keyword => !memo.TryGetXRefKeywordAt(keyword, out _)).Should().BeEmpty();
        read.Should().BeLessThan(bytes.Length + (2L * Piece), "each sweep reads the bytes before the keywords' piece once");
    }

    [Fact]
    public void The_memo_is_the_same_whatever_was_read_before_it_was_made()
    {
        // Object 4's row lies outside the file: reading it rebuilds the index. The memo is made by the first request for it —
        // before any read, after the rebuild, or after other reads —, and is the same each time.
        var file = Updated(out _);

        using var asked = PdfDocument.Open(file);
        var first = asked.Reader.GetScanMemo();
        asked.GetObject(new PdfObjectId(4));

        using var rebuilt = PdfDocument.Open(file);
        rebuilt.GetObject(new PdfObjectId(4));
        var second = rebuilt.Reader.GetScanMemo();

        using var read = PdfDocument.Open(file);
        read.GetObject(new PdfObjectId(1));
        read.GetObject(new PdfObjectId(3));
        var third = read.Reader.GetScanMemo();

        asked.WasRepaired.Should().BeTrue();
        rebuilt.WasRepaired.Should().BeTrue();
        read.WasRepaired.Should().BeFalse();
        asked.Reader.GetScanMemo().Should().BeSameAs(first);
        rebuilt.Reader.GetScanMemo().Should().BeSameAs(second);
        Describe(second, file.Length).Should().Equal(Describe(first, file.Length));
        Describe(third, file.Length).Should().Equal(Describe(first, file.Length));
        Headers(first, 0, long.MaxValue).Select(header => header.Number).Should().Equal(1, 2, 3, 5, 9, 3);
    }

    [Fact]
    public void A_rebuild_takes_the_headers_from_a_memo_made_before_it_reading_no_byte_for_its_scan_and_makes_none_itself()
    {
        // The same rebuild, run once the memo was asked for and once before: the same index, the same objects and the same
        // diagnostics, a definition met again among them; the rebuild that takes the memo reads the file a whole length less.
        // The one run first makes no memo: asked for after it, the memo is made by a scan of the file of its own, and is the
        // memo asked for first.
        var file = Updated(out _);

        var memoFirst = new StrictCountingSource(file);
        using var replayed = PdfDocument.Open(memoFirst, options: null, ownsSource: false);
        replayed.Reader.GetScanMemo();
        var before = memoFirst.BytesRead;
        replayed.GetObject(new PdfObjectId(4));
        var replaying = memoFirst.BytesRead - before;

        var rebuildFirst = new StrictCountingSource(file);
        using var scanned = PdfDocument.Open(rebuildFirst, options: null, ownsSource: false);
        before = rebuildFirst.BytesRead;
        scanned.GetObject(new PdfObjectId(4));
        var scanning = rebuildFirst.BytesRead - before;
        before = rebuildFirst.BytesRead;
        var madeAfter = scanned.Reader.GetScanMemo();
        var askingAfter = rebuildFirst.BytesRead - before;

        replayed.WasRepaired.Should().BeTrue();
        replayed.Diagnostics.Should().Equal(scanned.Diagnostics);
        replayed.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined);
        Entries(replayed).Should().Equal(Entries(scanned));
        replayed.GetObject(new PdfObjectId(3)).AsText().Should().Be("three, updated");
        scanning.Should().BeGreaterThanOrEqualTo(replaying + file.Length, "the rebuild that takes the memo scans nothing");
        askingAfter.Should().BeGreaterThanOrEqualTo(file.Length, "the rebuild run first made no memo");
        Describe(madeAfter, file.Length).Should().Equal(Describe(replayed.Reader.GetScanMemo(), file.Length));
    }

    [Fact]
    public void A_rebuild_with_the_memo_or_without_takes_the_headers_the_memo_s_scan_takes_past_where_a_length_taken_from_the_bytes_ends_a_stream_s_data()
    {
        // Stream 5's indirect length names object 8, whose value, 40, the value before the stream holds, ends the data at
        // its first header, a form's its text spells: the memo counts what the scan takes past there as taken between
        // objects. The data then holds a header of object 12 whose tokens a comment parts, and that comment holds the data's
        // first endstream, then an xref keyword, which only a scan for the memo looks for, and which it meets before that
        // header's obj: the memo puts the header back in its place, and a rebuild scans the file again. A string opened
        // before the header's number hides it between objects, and the comment the keyword, past where the load ends the
        // data, and the memo holds both as taken in the data; the scan reads the data as the bytes give it in either mode,
        // and each rebuild indexes the last header of each number the memo holds, the form's object 12 among them.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages), [12] = file.Object(12, "(early)") };
        rows[8] = file.Object(8, "40");
        rows[5] = file.Position;
        file.Raw("5 0 obj\n<< /Length 8 0 R >>\nstream\n% a form\n99 0 obj\n<< /FDF << /F (a.pdf) /Fields [] >> >>\nendobj\n");
        var attached = file.Position + "(x ".Length;
        file.Raw("(x 12 0\n%endstream xref\nobj (attached)\nendobj\n");
        var keyword = file.Position - "xref\nobj (attached)\nendobj\n".Length;
        file.Raw("endstream\nendobj\n");
        rows[4] = 999_999;
        file.Table(13, rows, "/Size 13 /Root 1 0 R");
        var bytes = file.Bytes;

        using var memoFirst = PdfDocument.Open(bytes);
        var memo = memoFirst.Reader.GetScanMemo();
        memoFirst.GetObject(new PdfObjectId(4));

        using var rebuildFirst = PdfDocument.Open(bytes);
        rebuildFirst.GetObject(new PdfObjectId(4));
        var madeAfter = rebuildFirst.Reader.GetScanMemo();

        memo.InScanOrder.Should().BeFalse();
        memo.TryGetHeaderAt(attached, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(attached, 12, 0, ScannedPlaceKind.Raw));
        memo.TryGetXRefKeywordAt(keyword, out var kind).Should().BeTrue();
        kind.Should().Be(ScannedPlaceKind.Raw);
        Describe(madeAfter, bytes.Length).Should().Equal(Describe(memo, bytes.Length));

        foreach (var document in new[] { memoFirst, rebuildFirst })
        {
            document.WasRepaired.Should().BeTrue();

            foreach (var taken in Headers(memo, 0, long.MaxValue))
            {
                document.Reader.Index.TryGet(taken.Number, out var entry).Should().BeTrue();

                if (Headers(memo, taken.Position + 1, long.MaxValue).All(later => later.Number != taken.Number))
                {
                    document.Reader.PositionOf(entry).Should().Be(taken.Position);
                }
            }

            document.GetObject(new PdfObjectId(12)).AsText().Should().Be("attached");
        }

        memoFirst.Diagnostics.Should().Equal(rebuildFirst.Diagnostics);
        Entries(memoFirst).Should().Equal(Entries(rebuildFirst));
    }

    [Fact]
    public void A_rebuild_scans_again_where_the_memo_does_not_hold_its_headers_in_the_order_the_scan_took_them()
    {
        // In the data of a stream whose length is an object, the scan takes a header of 8 in a comment, then another header
        // of 8, whose tokens run back across that comment: the memo puts the second before the first, and a rebuild scans
        // the file again, as one run before the memo was made does, so that the definition the scan takes last wins.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        var data = file.Position + "5 0 obj\n<< /Length 6 0 R >>\nstream\n".Length;
        file.Raw("5 0 obj\n<< /Length 6 0 R >>\nstream\n8 % 8 0 obj <\n0 obj (x)\nendstream\nendobj\n");
        rows[4] = 999_999;
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        var bytes = file.Bytes;

        var memoFirst = new StrictCountingSource(bytes);
        using var asked = PdfDocument.Open(memoFirst, options: null, ownsSource: false);
        var memo = asked.Reader.GetScanMemo();
        var before = memoFirst.BytesRead;
        asked.GetObject(new PdfObjectId(4));

        using var rebuilt = PdfDocument.Open(bytes);
        rebuilt.GetObject(new PdfObjectId(4));

        memo.InScanOrder.Should().BeFalse();
        Headers(memo, data, data + 20).Should().Equal(
            new ScannedHeader(data, 8, 0, ScannedPlaceKind.Raw), new ScannedHeader(data + 4, 8, 0, ScannedPlaceKind.Raw));
        asked.WasRepaired.Should().BeTrue();
        asked.Diagnostics.Should().Equal(rebuilt.Diagnostics);
        Entries(asked).Should().Equal(Entries(rebuilt));
        asked.Reader.Index.TryGet(8, out var entry).Should().BeTrue();
        asked.Reader.PositionOf(entry).Should().Be(data);
        (memoFirst.BytesRead - before).Should().BeGreaterThanOrEqualTo(bytes.Length, "the rebuild scans the file again");
    }

    [Theory]
    [InlineData("made before the rebuild", "headers repeated at varied distances")]
    [InlineData("made before the rebuild", "one header repeated at one distance")]
    [InlineData("asked for after the rebuild", "headers repeated at varied distances")]
    [InlineData("asked for after the rebuild", "one header repeated at one distance")]
    public void A_memo_past_its_bound_says_where_it_stops_being_whole_and_the_newest_definition_still_wins(string when, string repeats)
    {
        // MaxIndexEntries of 8: the index holds four numbers, while the scan takes the headers of 1 and 2, twenty older ones
        // of 3, the newest of 3, a keyword and a trailer keyword. The memo holds the first eight places, and stops being whole
        // where the ninth starts; the rebuild, run before the memo was asked for or after, scans the file, serves the newest
        // definition of 3, and reports no guard, as none cut what it serves. A caller that goes without the memo's word past
        // there reports the guard, once.
        var file = Repeated(repeats == "one header repeated at one distance", out var definitions);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } };

        using var document = PdfDocument.Open(file, options);
        var made = when == "made before the rebuild" ? document.Reader.GetScanMemo() : null;
        document.GetObject(new PdfObjectId(4));
        var memo = document.Reader.GetScanMemo();

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).AsText().Should().Be("newest");
        document.Diagnostics.Should().NotContain(entry => entry.Code.StartsWith("limit.", StringComparison.Ordinal));
        (made ?? memo).Should().BeSameAs(memo);
        memo.IsWhole.Should().BeFalse();
        memo.Count.Should().Be(8);
        memo.FullAt.Should().Be(definitions[6]);
        memo.WholeUntil.Should().BeGreaterThan(definitions[5]).And.BeLessThanOrEqualTo(definitions[6]);
        memo.Trailers.Should().BeNull();
        memo.TryGetHeaderAt(definitions[5], out _).Should().BeTrue();
        memo.TryGetHeaderAt(definitions[6], out _).Should().BeFalse();
        Headers(memo, 0, long.MaxValue).Should().HaveCount(8);

        document.Reader.ReachScanMemoBound([definitions[5], definitions[6]]);
        document.Reader.ReachScanMemoBound([definitions[6]]);

        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries).Which;
        report.Position.Should().Be(definitions[6]);
        report.Message.Should().Be(
            "The scan for objects recorded 8 object headers and keywords; those past them were not recorded. Raise PdfReaderLimits.MaxIndexEntries to read past it.");
    }

    [Fact]
    public void A_memo_that_fills_once_an_attached_file_ended_the_wait_for_a_length_s_object_is_whole_up_to_the_first_place_it_left_out()
    {
        // Stream 5 lost its endstream, and object 8, which holds its length, is lost: the scan awaits it past object 9's
        // header, in the data. Object 12, an attached file whose length object 13 holds a length no endstream follows, ends
        // that wait. MaxIndexEntries of 10: the memo fills at object 24's header, among the values after them, and is whole
        // up to there, not only before stream 5's data, whose places no length's object can turn any more.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        var start = file.Position;
        var text = "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n9 0 obj (nine) endobj\n" +
            "12 0 obj\n<< /Type /EmbeddedFile /Length 13 0 R >>\nstream\n%PDF-1.4\nhello\nendstream\nendobj\n13 0 obj\n3\nendobj\n" +
            string.Concat(Enumerable.Range(20, 20).Select(number => string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n({number})\nendobj\n")));
        file.Raw(text);
        file.Table(3, rows, "/Size 3 /Root 1 0 R");

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 10 } });
        var memo = document.Reader.GetScanMemo();

        memo.IsWhole.Should().BeFalse();
        memo.FullAt.Should().Be(start + text.IndexOf("24 0 obj", StringComparison.Ordinal));
        memo.WholeUntil.Should().BeGreaterThan(start + text.IndexOf("23 0 obj", StringComparison.Ordinal)).And.BeLessThanOrEqualTo(memo.FullAt);
    }

    [Fact]
    public void A_memo_that_fills_past_an_attached_file_whose_length_s_object_before_it_shows_where_its_data_ends_is_whole_up_to_the_first_place_it_left_out()
    {
        // Attached file 12 lost its endstream; object 13, before it, holds its length, which an endobj follows: the length shows
        // where the data ends, and the scan awaits no other object 13. MaxIndexEntries of 10: the memo fills among the values
        // after the attached file, and is whole up to the first place it left out, not only before the attached file's data.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        var start = file.Position;
        var text = "13 0 obj\n15\nendobj\n12 0 obj\n<< /Type /EmbeddedFile /Length 13 0 R >>\nstream\n%PDF-1.4\nhello\nendobj\n" +
            string.Concat(Enumerable.Range(20, 20).Select(number => string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n({number})\nendobj\n")));
        file.Raw(text);
        file.Table(3, rows, "/Size 3 /Root 1 0 R");

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 10 } });
        var memo = document.Reader.GetScanMemo();

        memo.IsWhole.Should().BeFalse();
        memo.FullAt.Should().BeGreaterThan(start + text.IndexOf("20 0 obj", StringComparison.Ordinal));
        memo.WholeUntil.Should().BeGreaterThan(start + text.IndexOf("20 0 obj", StringComparison.Ordinal)).And.BeLessThanOrEqualTo(memo.FullAt);
    }

    [Fact]
    public void An_updated_file_whose_places_outnumber_the_bound_but_not_its_numbers_rebuilds_with_no_guard_reached()
    {
        // Three objects, the third defined again by two updates, each with its table and trailer: eleven places for three
        // numbers. Under a bound of 5, opened to throw on a guard, the rebuild the broken startxref sets off throws nothing,
        // and serves the newest definition.
        var file = new StringBuilder("%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n3 0 obj\n(first)\nendobj\n");

        foreach (var update in new[] { "second", "third" })
        {
            file.Append("xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Root 1 0 R /Size 4 >>\nstartxref\n999999\n%%EOF\n")
                .Append(CultureInfo.InvariantCulture, $"3 0 obj\n({update})\nendobj\n");
        }

        file.Append("xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Root 1 0 R /Size 4 >>\nstartxref\n999999\n%%EOF\n");
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxIndexEntries = 5 } };

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(file.ToString()), options);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).AsText().Should().Be("third");
        document.Diagnostics.Should().NotContain(entry => entry.Code.StartsWith("limit.", StringComparison.Ordinal));
        document.Reader.GetScanMemo().IsWhole.Should().BeFalse();
    }

    [Fact]
    public void The_memo_reports_its_bound_apart_from_the_index_which_reports_its_own_where_both_fill()
    {
        // Objects 1 to 10, a table for 1 to 3 whose row for 3 lies outside the file: under a bound of 5, the memo, asked for
        // first, and the index the read of 3 rebuilds both fill at the header of 6. The index reports its bound as the
        // rebuild reaches it, and the memo its own once a caller goes without its word: neither hides the other.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages), [3] = 999_999 };
        var sixth = 0L;

        for (var number = 3; number <= 10; number++)
        {
            var at = file.Object(number, string.Create(CultureInfo.InvariantCulture, $"(object {number})"));
            sixth = number == 6 ? at : sixth;
        }

        file.Table(4, rows, "/Size 4 /Root 1 0 R");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 5 } };

        using var document = PdfDocument.Open(file.Bytes, options);
        var memo = document.Reader.GetScanMemo();
        document.GetObject(new PdfObjectId(3)).AsText().Should().Be("object 3");
        document.Reader.ReachScanMemoBound([sixth]);

        memo.FullAt.Should().Be(sixth);
        document.WasRepaired.Should().BeTrue();
        var reports = document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries).ToList();
        reports.Select(entry => entry.Position).Should().Equal(sixth, sixth);
        reports[0].Message.Should().StartWith("The index reached 5 entries;");
        reports[1].Message.Should().StartWith("The scan for objects recorded 5 object headers and keywords;");
    }

    [Fact]
    public void A_memo_past_its_bound_throws_only_where_a_caller_goes_without_it_for_a_document_opened_to_throw_on_a_guard()
    {
        // Asked for, the memo throws nothing as it is made; the caller that goes without its word throws each time. Asked for
        // after a rebuild, which serves the newest definition, it throws nothing either.
        var file = Repeated(alike: false, out var definitions);
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } };

        using (var asked = PdfDocument.Open(file, options))
        {
            asked.Reader.GetScanMemo().FullAt.Should().Be(definitions[6]);

            for (var call = 0; call < 2; call++)
            {
                var thrown = FluentActions.Invoking(() => asked.Reader.ReachScanMemoBound([definitions[6]])).Should().Throw<PdfLimitExceededException>().Which;
                thrown.LimitName.Should().Be(nameof(PdfReaderLimits.MaxIndexEntries));
                thrown.Position.Should().Be(definitions[6]);
            }
        }

        using var rebuilt = PdfDocument.Open(file, options);
        rebuilt.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfNull>();
        rebuilt.GetObject(new PdfObjectId(3)).AsText().Should().Be("newest");
        rebuilt.Reader.GetScanMemo().IsWhole.Should().BeFalse();
    }

    [Fact]
    public void A_memo_that_stops_being_whole_at_the_file_s_first_byte_reports_its_bound_and_a_rebuild_after_it_scans_the_file_again()
    {
        // Before its header, the file starts with object 7's header, whose number and generation eight lines part, each an
        // end-of-file marker an xref keyword is glued to: a load lexes each line as a comment, while the scan takes each
        // keyword as a token, then the header, which starts at the file's first byte, ninth. Under a bound of 8, the memo
        // stops being whole there and holds no header. Object 5's row lies 3 bytes past its header: the search near it makes
        // the memo, serves the header as the bytes give it, and reports the bound where the memo stopped. Object 4's row lies
        // outside the file: its read rebuilds the index, which takes no header from a memo that is not whole, scans the file
        // again, and serves every object, object 7 before the header among them.
        var junk = "7\n" + string.Concat(Enumerable.Repeat("%%EOFxref\n", 8)) + "0 obj\n(seven)\nendobj\n";
        var file = new HandWrittenFile(prefix: junk);
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        rows[5] = file.Object(5, "(five)") + 3;
        rows[4] = 999_999;
        file.Table(8, rows, "/Size 8 /Root 1 0 R");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } };

        using var document = PdfDocument.Open(file.Bytes, options);

        document.GetObject(new PdfObjectId(5)).AsText().Should().Be("five");
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries).Which.Position.Should().Be(0);

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfNull>();

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(5)).AsText().Should().Be("five");
        document.GetObject(new PdfObjectId(7)).AsText().Should().Be("seven");
    }

    [Theory]
    [InlineData("an object header")]
    [InlineData("an xref keyword")]
    [InlineData("a trailer keyword")]
    public void A_scan_for_the_memo_reads_no_further_than_the_first_place_the_memo_cannot_hold(string place)
    {
        // Past the catalog and the page tree, ten places of one kind — definitions of object 3, xref keywords or trailer
        // keywords —, then comment lines to 2 MiB, and the table. Under a bound of 8, the memo holds the two headers and the
        // first six places, and leaves out the seventh, in the scan's first piece: the scan stops there, having read that
        // piece alone, rather than reading on to the end of the file for places the memo can no longer hold.
        const int Piece = 1 << 20;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        var places = new List<long>();

        for (var index = 0; index < 10; index++)
        {
            places.Add(file.Position);

            if (place == "an object header")
            {
                file.Object(3, string.Create(CultureInfo.InvariantCulture, $"({index})"));
            }
            else
            {
                file.Raw(place == "an xref keyword" ? "xref\n" : "trailer\n");
            }
        }

        PadWithComments(file, 2 * Piece);
        file.Table(3, rows, "/Size 3 /Root 1 0 R");
        var source = new StrictCountingSource(file.Bytes);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);

        var before = source.BytesRead;
        var memo = document.Reader.GetScanMemo();
        var read = source.BytesRead - before;

        (memo.IsWhole, memo.FullAt).Should().Be((false, places[6]));
        read.Should().BeLessThanOrEqualTo(Piece, "the scan reads its first piece, and stops at the place the memo leaves out");
    }

    [Theory]
    [InlineData("past where the memo is whole", false)]
    [InlineData("past where the memo is whole", true)]
    [InlineData("past where the memo is whole and past the first piece the scan reads", false)]
    [InlineData("past where the memo is whole and past the first piece the scan reads", true)]
    [InlineData("where the memo stops being whole, glued to the value of the object before it", false)]
    [InlineData("where the memo stops being whole, glued to the value of the object before it", true)]
    [InlineData("before where the memo is whole, the bytes searched reaching past it", false)]
    [InlineData("before where the memo is whole, the bytes searched reaching past it", true)]
    [InlineData("past where a source that gives fewer bytes than asked ended the scan", false)]
    [InlineData("past where a source that gives fewer bytes than asked ended the scan", true)]
    [InlineData("before where the memo is whole, past a stream whose indirect length's object the scan met before the memo filled", false)]
    [InlineData("before where the memo is whole, past a stream whose indirect length's object the scan met before the memo filled", true)]
    public void The_search_near_a_row_reports_the_memo_s_bound_only_where_it_finds_a_header_past_where_the_memo_is_whole(
        string header, bool throwOnLimit)
    {
        // MaxIndexEntries of 8: the scan takes the headers of 1 and 2, then ten definitions of 3, and object 4's header
        // after them or before; the memo holds no place past the eighth the scan takes. Object 4's header lies 5 bytes
        // before its row. Past there, the search near the row goes without the memo's word for it, reports the memo's
        // bound, and serves the header as the bytes give it, a choice it cannot vouch for; opened to throw on a guard,
        // the read throws there. Glued to the value of the sixth definition of 3, which no endobj ends, object 4's
        // header is the ninth place the scan takes and starts where that value ends: the memo stops being whole just
        // where the header starts, and the search goes without its word there as further on. Before where the memo is
        // whole, the memo answers for it, though the bytes searched reach past there: the bound changed nothing read,
        // and is not reported. So it does past the data of a stream that lost its endstream, whose indirect length's
        // object the scan awaited from object 4's header and met after it, before the memo filled: the wait over, where
        // that data starts bounds the memo no more. Past 6,000 bytes of comment read through a source that gives at
        // most 4,096 bytes a read, the memo is whole but holds nothing past where the scan's short window ended it
        // (#52): the header is served as the bytes give it all the same, and no guard is reported or thrown, none
        // having cut the memo. The validator's finding names what stopped the memo. Past 1.1 MB of comment, beyond the first
        // piece the scan reads, which the source gives whole, the memo's bound alone keeps it from answering all the same:
        // the bound is reported, as within that piece.
        var shortReads = header == "past where a source that gives fewer bytes than asked ended the scan";
        var past = !header.StartsWith("before", StringComparison.Ordinal);
        var glued = header == "where the memo stops being whole, glued to the value of the object before it";
        var boundReached = past && !shortReads;
        var four = "4 0 obj\n(four)\nendobj\n";
        var threes = Enumerable.Range(0, 10).Select(index => string.Create(CultureInfo.InvariantCulture, $"3 0 obj\n({index})\nendobj\n")).ToArray();
        var body = header switch
        {
            "past where a source that gives fewer bytes than asked ended the scan" => "%" + new string('-', 5998) + "\n" + four,
            "before where the memo is whole, the bytes searched reaching past it" => four + string.Concat(threes),
            "past where the memo is whole" => string.Concat(threes) + four,
            "past where the memo is whole and past the first piece the scan reads" => string.Concat(threes) + "%" + new string('-', 1_100_000) + "\n" + four,
            "where the memo stops being whole, glued to the value of the object before it" => string.Concat(threes[..5]) + "3 0 obj\n(5)" + four,
            _ => "5 0 obj\n<< /Length 6 0 R >>\nstream\n0123456789\nendobj\n" + four + "6 0 obj\n10\nendobj\n" + string.Concat(threes),
        };
        var (file, row) = NearbyFiles.FourNear(body, "4 0 obj", 5);
        var options = new PdfReaderOptions { ThrowOnLimit = throwOnLimit, Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } };
        PdfFileSource source = shortReads ? new CappedReadSource(file, 4096) : PdfFileSource.FromMemory(file);

        using var document = PdfDocument.Open(source, options, ownsSource: true);

        if (boundReached && throwOnLimit)
        {
            FluentActions.Invoking(() => document.GetObject(new PdfObjectId(4))).Should().Throw<PdfLimitExceededException>()
                .Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxIndexEntries));
            return;
        }

        document.GetObject(new PdfObjectId(4)).AsText().Should().Be("four");
        var memo = document.Reader.GetScanMemo();
        var adjusted = (PdfDiagnosticCodes.XRefOffsetAdjusted, row - 5);
        IEnumerable<(string, long)> reported = boundReached ? [(PdfDiagnosticCodes.LimitIndexEntries, memo.FullAt), adjusted] : [adjusted];

        if (glued)
        {
            (memo.FullAt, memo.WholeUntil).Should().Be((row - 5, row - 5));
        }

        document.WasRepaired.Should().BeFalse();
        memo.IsWhole.Should().Be(shortReads);
        memo.WholeUntil.Should().BeLessThan(row + PdfFileReader.NearbySearchRadius, "the bytes searched reach past where the memo is whole");
        (row - 5 >= memo.WholeUntil).Should().Be(past);
        document.Diagnostics.Select(entry => (entry.Code, entry.Position)).Should().Equal(reported);
        var finding = new PdfValidator().Validate(document).Findings.Should().ContainSingle(finding => finding.Location.Object == new PdfObjectId(4)).Which;
        (finding.RuleId, finding.Severity).Should().Be(past
            ? (PdfValidationRuleIds.XRefEntryUnconfirmed, PdfValidationSeverity.Error)
            : (PdfValidationRuleIds.XRefEntryShifted, PdfValidationSeverity.Warning));
        finding.Message.Should().EndWith(shortReads
            ? "which it cannot tell lies between objects: it lies past the last object its scan for objects passed before the end of a read that a source answered with fewer bytes than asked, or, where that read ended while the scan awaited the object of a stream's indirect /Length, past that stream's data's start."
            : past
                ? "which it cannot tell lies between objects: it lies past the places its scan for objects records within PdfReaderLimits.MaxIndexEntries, or, where the scan reached that bound while it awaited the object of a stream's indirect /Length, past that stream's data's start. Raising the limit lets it tell."
                : string.Create(CultureInfo.InvariantCulture, $"the object starts 5 bytes before it, at offset {row - 5}."));
    }

    [Theory]
    [InlineData("the entry after object 4's", false)]
    [InlineData("the entry after object 4's", true)]
    [InlineData("the entry before object 4's", false)]
    [InlineData("the entry before object 4's", true)]
    public void The_memo_s_bound_for_a_header_found_near_a_row_is_reported_by_settling_as_a_load_reports_it_and_never_by_the_probe_of_the_entries(
        string outside, bool throwOnLimit)
    {
        // MaxIndexEntries of 8: the scan takes the headers of 1 and 2, then ten definitions of 3, and object 4's header, past
        // the eighth place, 5 bytes before its row. Nothing is read before the validation, which settles the index before its
        // first rule: the entry of object 5, or of object 3, places its object outside the file and has the index rebuilt, so
        // that no rule loads object 4. Settling reaches 5's entry after 4's: it has searched near 4's row, without the memo's
        // word, and reported the memo's bound as a load does; opened to throw on a guard, a load throws there, and settling
        // leaves the guard to a load. Settling reaches 3's entry first, and searches nowhere near 4's row. The probe
        // of the entries searches near the row in its turn: it says the entry is unconfirmed, and neither reports the bound
        // nor throws, whatever settling did.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };

        for (var index = 0; index < 10; index++)
        {
            rows.TryAdd(3, file.Object(3, string.Create(CultureInfo.InvariantCulture, $"({index})")));
        }

        var four = file.Object(4, "(four)");
        rows[4] = four + 5;
        rows[outside == "the entry after object 4's" ? 5 : 3] = 999_999;
        file.Table(6, rows, "/Size 6 /Root 1 0 R");
        var options = new PdfReaderOptions { ThrowOnLimit = throwOnLimit, Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        var report = new PdfValidator().Validate(document);

        var memo = document.Reader.GetScanMemo();
        IEnumerable<long> reported = outside == "the entry after object 4's" && !throwOnLimit ? [memo.FullAt] : [];
        (document.WasRepaired, memo.IsPastBound(four)).Should().Be((true, true));
        document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries).Select(entry => entry.Position).Should().Equal(reported);
        var finding = report.Findings.Should().ContainSingle(finding => finding.Location.Object == new PdfObjectId(4)).Which;
        (finding.RuleId, finding.Location).Should().Be(
            (PdfValidationRuleIds.XRefEntryUnconfirmed, PdfValidationLocation.OfObject(new PdfObjectId(4), rows[4])));
    }

    [Theory]
    [InlineData("its bound, then a short read", false)]
    [InlineData("its bound, then a short read", true)]
    [InlineData("its bound, then a short read that ends where object 4's header starts", false)]
    [InlineData("its bound, then a short read that ends where object 4's header starts", true)]
    [InlineData("a header of 4 in a string, its bound, then a short read", false)]
    [InlineData("a header of 4 in a string, its bound, then a short read", true)]
    [InlineData("a short read, then its bound", false)]
    [InlineData("a short read, then its bound", true)]
    public void The_memo_s_bound_is_neither_reported_nor_blamed_for_a_header_past_where_a_short_read_ended_the_memo(string stops, bool throwOnLimit)
    {
        // The memo fills within a piece the source gives short, 4,096 bytes a read, before object 4's header, which lies past
        // the piece, or starts just where it ends, the first byte it does not hold; or, read through a source that gives at
        // most 1 MiB a read, the walk back from object 5's obj keyword, whose number lies 300,000 spaces before it, comes back
        // short first, and the memo fills afterwards, past object 4's header. Object 4's row lies 3 bytes past its header.
        // Raising MaxIndexEntries would not let the memo answer there: the search serves the header as the bytes give it,
        // reports no guard, throws none, and the validator's finding names the short read (#52). A string before it may hold
        // a header of 4 within the search's reach and before where the memo is whole, which the memo does not take, the memo
        // filling at the definition of 5 the piece's end cuts, just before object 4's header: the search drops that header,
        // and the bound is not reported for it either.
        const int Cap = 4096;
        var boundFirst = stops != "a short read, then its bound";
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        var pagesEnd = rows[2] + "2 0 obj\n".Length + Pages.Length;
        var inString = -1L;

        if (stops == "a header of 4 in a string, its bound, then a short read")
        {
            // Object 6's string holds the header on a line of its own; of the eighteen definitions of 5 after it, the last,
            // the place past the bound, ends 4 bytes past the piece.
            var fives = string.Concat(Enumerable.Range(0, 18).Select(index => string.Create(CultureInfo.InvariantCulture, $"5 0 obj\n({index})\nendobj\n")));
            var padding = (int)(Cap + 4 - file.Position - fives.Length - "6 0 obj\n(\n4 0 obj\n)\nendobj\n".Length);
            inString = file.Position + "6 0 obj\n(".Length + padding + 1;
            file.Object(6, "(" + new string('x', padding) + "\n4 0 obj\n)");
            file.Raw(fives);
        }
        else if (boundFirst)
        {
            for (var index = 0; index < 30; index++)
            {
                file.Object(5, string.Create(CultureInfo.InvariantCulture, $"({index})"));
            }

            file.Padding(6, stops == "its bound, then a short read" ? 6000 : (int)(Cap - file.Position - "6 0 obj\n()\nendobj\n".Length));
        }
        else
        {
            file.Raw(new string('x', 800_000) + "\n");
            file.Raw("5" + new string(' ', 300_000) + "0 obj\n(five)\nendobj\n");
        }

        var four = file.Object(4, "(four)");
        rows[4] = four + 3;

        for (var index = 0; index < (boundFirst ? 0 : 10); index++)
        {
            file.Object(8, string.Create(CultureInfo.InvariantCulture, $"({index})"));
        }

        file.Table(9, rows, "/Size 9 /Root 1 0 R");
        var options = new PdfReaderOptions { ThrowOnLimit = throwOnLimit, Limits = PdfReaderLimits.Default with { MaxIndexEntries = boundFirst ? 20 : 12 } };
        using var document = PdfDocument.Open(new CappedReadSource(file.Bytes, boundFirst ? Cap : 1 << 20), options, ownsSource: true);

        var served = document.GetObject(new PdfObjectId(4)).AsText();
        var memo = document.Reader.GetScanMemo();

        if (stops == "its bound, then a short read that ends where object 4's header starts")
        {
            four.Should().Be(Cap);
        }

        if (inString >= 0)
        {
            inString.Should().BeInRange(rows[4] - PdfFileReader.NearbySearchRadius, memo.WholeUntil - 1);
        }

        (served, document.WasRepaired).Should().Be(("four", false));
        (memo.IsWhole, four >= memo.WholeUntil, memo.IsPastBound(four)).Should().Be((false, true, false));
        (memo.FullAt < four).Should().Be(boundFirst);
        document.Diagnostics.Select(entry => (entry.Code, entry.Position)).Should().Equal((PdfDiagnosticCodes.XRefOffsetAdjusted, four));
        var finding = new PdfValidator().Validate(document).Findings.Should().ContainSingle(finding => finding.Location.Object == new PdfObjectId(4)).Which;
        (finding.RuleId, finding.Severity).Should().Be((PdfValidationRuleIds.XRefEntryUnconfirmed, PdfValidationSeverity.Error));
        finding.Message.Should().EndWith("it lies past the last object its scan for objects passed before the end of a read that a source answered with fewer bytes than asked, or, where that read ended while the scan awaited the object of a stream's indirect /Length, past that stream's data's start.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Settling_before_validation_reports_the_memo_s_bound_as_a_load_does_and_throws_nothing_for_a_document_opened_to_throw(bool throwOnLimit)
    {
        // MaxIndexEntries of 8: the scan takes the headers of 1 and 2, then ten definitions of 3, then object 4's header, past
        // the eighth place, 5 bytes before its row; object 5's row lies outside the file. Validation settles the index before
        // its first rule: the search near 4's row goes without the memo's word, and reports the memo's bound as a load does,
        // before 5's row has the index rebuilt, which places object 4 at its header, where no rule reads it near its row.
        // Opened to throw on a guard, the document throws nothing there, nor in the probe of the entries, and the finding
        // names the bound.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };

        for (var index = 0; index < 10; index++)
        {
            rows.TryAdd(3, file.Object(3, string.Create(CultureInfo.InvariantCulture, $"({index})")));
        }

        var four = file.Object(4, "(four)");
        rows[4] = four + 5;
        rows[5] = 999_999;
        file.Table(6, rows, "/Size 6 /Root 1 0 R");
        var options = new PdfReaderOptions { ThrowOnLimit = throwOnLimit, Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        var report = new PdfValidator().Validate(document);

        var memo = document.Reader.GetScanMemo();
        IEnumerable<long> reported = throwOnLimit ? [] : [memo.FullAt];
        (document.WasRepaired, memo.IsPastBound(four)).Should().Be((true, true));
        document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries).Select(entry => entry.Position).Should().Equal(reported);
        var finding = report.Findings.Should().ContainSingle(finding => finding.Location.Object == new PdfObjectId(4)).Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefEntryUnconfirmed);
        finding.Message.Should().EndWith("it lies past the places its scan for objects records within PdfReaderLimits.MaxIndexEntries, or, where the scan reached that bound while it awaited the object of a stream's indirect /Length, past that stream's data's start. Raising the limit lets it tell.");
    }

    [Theory]
    [InlineData(0, 6_000, 5)]
    [InlineData(4096, 6_000, 5)]
    [InlineData(0, 1_200_000, 5)]
    [InlineData(0, 1_040_000, 20_000)]
    public void The_memo_holds_no_place_past_where_a_source_that_gives_fewer_bytes_than_asked_ended_the_scan(int cap, int comment, int value)
    {
        // Past the catalog and the page tree, 6,000 bytes of comment, or a megabyte and more that the scan's sweep reads back
        // from the piece before object 3's, come before object 3, a string, read through a source that gives every byte asked
        // for, or at most 4,096 bytes a read, as a source over a store of blocks may. The scan takes a window shorter than
        // asked for the end of the data there, and its memo, whole, holds nothing past it, nor past where the page tree's
        // value ends, as far back as a header whose obj lies past the end may start: no place there is vouched for. Read
        // whole, the memo holds every place to the file's end, the sweep's whole reads behind its piece included, and the
        // whole read again from its header of a string that runs past the scan's piece (#52).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        file.Raw("%" + new string('-', comment - 2) + "\n");
        rows[3] = file.Padding(3, value);
        file.Table(4, rows, "/Size 4 /Root 1 0 R");
        PdfFileSource source = cap > 0 ? new CappedReadSource(file.Bytes, cap) : PdfFileSource.FromMemory(file.Bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: true);

        var memo = document.Reader.GetScanMemo();
        var pagesEnd = rows[2] + "2 0 obj\n".Length + Pages.Length;

        (memo.IsWhole, memo.WholeUntil, memo.TryGetHeaderAt(rows[3], out _)).Should().Be(cap > 0 ? (true, pagesEnd, false) : (true, long.MaxValue, true));
    }

    [Fact]
    public void A_keyword_whose_line_a_short_read_keeps_from_telling_ends_no_awaited_data()
    {
        // Stream 5 lost its endstream and its length's object: an xref keyword that starts its line, white space before it,
        // ends its data for the memo's kinds, and object 9's header past it, glued to a sign, is held as taken between objects.
        // The file is cut short as the bytes before the keyword are read, a byte before it, the source giving fewer than asked:
        // whether the keyword starts its line is not told, it ends no data, and object 9's header stays held as taken in the
        // data (#52, #372).
        const string Text = "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\n  xref\n+9 0 obj\n(nine)\n10 0 obj\n(ten)\nendobj\n";
        var (file, at) = Sound(Text, Text.IndexOf("+9", StringComparison.Ordinal));
        var keyword = at - "xref\n".Length;
        using var whole = PdfDocument.Open(file);
        using var source = new CutShortSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        source.CutWhenReadAt(keyword - PdfObjectParser.EndObjLookahead, (int)keyword - 1);

        whole.Reader.GetScanMemo().TryGetHeaderAt(at, out var sound).Should().BeTrue();
        document.Reader.GetScanMemo().TryGetHeaderAt(at, out var cut).Should().BeTrue();

        (sound.Kind, cut.Kind).Should().Be((ScannedPlaceKind.Clean, ScannedPlaceKind.Raw));
    }

    [Fact]
    public void A_header_that_ends_a_cut_file_where_an_attached_file_s_direct_length_ends_its_data_is_held_as_taken_between_objects()
    {
        // Attached file 5 lost its endstream and endobj, and the file, which holds no endstream, is cut right after object 9's
        // header, where the attached file's direct length ends its data: that header, which the bytes end, ends the stream's
        // object, and is held as taken between objects, the places within the data as taken there (#366).
        const string Data = "%PDF-1.4\n7 0 obj (seven) endobj\n%%EOF\n";
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"%PDF-1.7\n1 0 obj\n{Catalog}\nendobj\n2 0 obj\n{Pages}\nendobj\n5 0 obj\n<< /Type /EmbeddedFile /Length {Data.Length} >>\nstream\n{Data}9 0 obj");

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));
        var memo = document.Reader.GetScanMemo();

        memo.TryGetHeaderAt(text.IndexOf("7 0 obj", StringComparison.Ordinal), out var inner).Should().BeTrue();
        memo.TryGetHeaderAt(text.IndexOf("9 0 obj", StringComparison.Ordinal), out var cut).Should().BeTrue();
        (inner.Kind, cut.Kind).Should().Be((ScannedPlaceKind.Raw, ScannedPlaceKind.Clean));
    }

    [Fact]
    public void A_keyword_that_starts_its_line_near_the_start_of_the_file_ends_the_data_of_a_stream_whose_length_s_object_is_lost()
    {
        // Stream 5, the file's first object, lost its endstream and its length's object; an xref keyword that starts its line
        // in its data, fewer than 64 bytes past the start of the file, ends the data for the memo's kinds, the bytes before it
        // read from the file's first, and object 9's header past it, glued to a sign, is held as taken between objects (#372).
        const string Text = "5 0 obj\n<< /Length 8 0 R >>\nstream\nxref\n+9 0 obj\n(nine)\n10 0 obj\n(ten)\nendobj\n";
        var pdf = new HandWrittenFile();
        var at = pdf.Position + Text.IndexOf("+9", StringComparison.Ordinal);
        pdf.Raw(Text);
        var rows = new Dictionary<int, long> { [1] = pdf.Object(1, Catalog), [2] = pdf.Object(2, Pages) };
        pdf.Table(3, rows, "/Size 3 /Root 1 0 R");

        using var document = PdfDocument.Open(pdf.Bytes);
        var memo = document.Reader.GetScanMemo();

        (at - "xref\n".Length).Should().BeLessThan(PdfObjectParser.EndObjLookahead);
        memo.TryGetHeaderAt(at, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(at, 9, 0, ScannedPlaceKind.Clean));
    }

    [Fact]
    public void A_place_whose_distance_from_the_line_of_a_data_s_first_endstream_a_short_read_keeps_from_telling_is_held_as_taken_in_the_data()
    {
        // Stream 5's direct length ends its data before an endobj, no endstream following: the scan reads the data up to its
        // first endstream, which a comment past object 4 holds, and the memo learns whether object 9's header, past a comment
        // longer than the scan's piece, lies on that comment's line by reading the bytes from there, past what the piece holds. Read
        // whole, an end of line comes first, and the header is held as taken between objects. The file is cut short as those
        // bytes are read, the source giving fewer than asked and no end of line: the header is held as taken in the data.
        var text = "5 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n% endstream\n%" + new string('-', 1_100_000) +
            "\n9 0 obj\n(nine)\nendobj\n";
        var (file, at) = Sound(text, text.IndexOf("9 0 obj", StringComparison.Ordinal));
        var endStream = Encoding.Latin1.GetString(file).IndexOf("endstream", StringComparison.Ordinal);
        using var whole = PdfDocument.Open(file);
        using var source = new CutShortSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        source.CutWhenReadAt(endStream - 1, endStream + 5);

        whole.Reader.GetScanMemo().TryGetHeaderAt(at, out var sound).Should().BeTrue();
        document.Reader.GetScanMemo().TryGetHeaderAt(at, out var cut).Should().BeTrue();

        (sound.Kind, cut.Kind).Should().Be((ScannedPlaceKind.Clean, ScannedPlaceKind.Raw));
    }

    [Fact]
    public void A_place_whose_line_a_read_the_source_gives_short_keeps_from_telling_is_held_as_taken_in_the_data_though_later_reads_are_whole()
    {
        // Stream 5's direct length ends its data before an endobj, no endstream following: its first endstream lies in a
        // comment, on a line where a string of object 7 opens and runs over the end of the piece the scan holds, the comment's
        // line ending past it. Object 8's header lies on the line that string ends, which a comment there would hold: the memo
        // learns whether it lies on the endstream's line by reading the bytes from object 7's header, past what the piece
        // holds. Read whole, an end of line comes first, and the header is held as taken between objects. The source giving
        // fewer bytes than asked to the reads that start there, and whole reads after them, the search stops: whether the
        // header lies on that line is not told, and it is held as taken in the data, as the sweep from where a load ends the
        // data reads it (#52).
        var text = "5 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n% endstream 7 0 obj (a" + new string('x', 150) +
            "\n%) endobj 8 0 obj (eight) endobj\n";
        text = CommentLines(1_048_450 - 109 - text.IndexOf("% endstream", StringComparison.Ordinal)) + text;
        var (file, at) = Sound(text, text.IndexOf("8 0 obj", StringComparison.Ordinal));
        var seven = Encoding.Latin1.GetString(file).IndexOf("7 0 obj (a", StringComparison.Ordinal);
        var armed = false;
        using var whole = PdfDocument.Open(file);
        using var source = new ShortCountSource(file, (start, count) => armed && start == seven - 1 ? count / 2 : count);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        armed = true;

        whole.Reader.GetScanMemo().TryGetHeaderAt(at, out var sound).Should().BeTrue();
        document.Reader.GetScanMemo().TryGetHeaderAt(at, out var cut).Should().BeTrue();

        (sound.Kind, cut.Kind).Should().Be((ScannedPlaceKind.Clean, ScannedPlaceKind.Raw));
        seven.Should().BeLessThan((1024 * 1024) - PdfObjectParser.EndObjLookahead);
        source.ShortReads.Should().BePositive();
    }

    [Theory]
    [InlineData("half of what it asks")]
    [InlineData("ten bytes")]
    public void A_search_for_a_length_s_object_that_a_read_the_source_gives_short_stops_confirms_nothing_past_it(string given)
    {
        // Object 6, which holds the length of stream 5's data, lies nearly a mebibyte past the data's first endstream, past the
        // piece the scan holds: the search reads it from the source, and confirms the stream, its data's header not taken.
        // The source giving fewer bytes than asked to the read that crosses the piece's end, half of them or fewer than the
        // bytes it repeats of the read before, the search stops there, and the scan reads the data as the bytes give it, object
        // 9 taken there, though the reads after it would be whole (#363).
        var text = "5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n" + CommentLines(1_048_400) + "6 0 obj\n22\nendobj\n";
        var (file, at) = Sound(text, text.IndexOf("9 0 obj", StringComparison.Ordinal));
        var armed = false;
        using var whole = PdfDocument.Open(file);
        using var source = new ShortCountSource(file, (start, count) =>
            armed && start < 1024 * 1024 && start + count > 1024 * 1024 ? given == "ten bytes" ? 10 : count / 2 : count);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        armed = true;

        whole.Reader.GetScanMemo().TryGetHeaderAt(at, out _).Should().BeFalse();
        document.Reader.GetScanMemo().TryGetHeaderAt(at, out var header).Should().BeTrue();
        header.Kind.Should().Be(ScannedPlaceKind.Raw);
        source.ShortReads.Should().BePositive();
    }

    [Fact]
    public void A_memo_a_short_read_ends_while_a_stream_s_length_is_awaited_holds_before_where_it_is_whole_what_a_whole_read_does()
    {
        // Object 5 lost its endstream, and the object that holds its length lies past 6,000 bytes of comment, past the end of
        // the first read a source that gives at most 4,096 bytes a read answers: read whole, the memo holds object 9's header,
        // between the two, as taken between objects; read short, it is whole only before object 5's data, where the scan
        // would have held it so too (#52).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        file.Raw("5 0 obj\n<< /Length 8 0 R >>\nstream\n");
        var data = file.Position;
        file.Raw("0123456789\nendobj\n");
        rows[9] = file.Object(9, "(nine)");
        file.Raw("%" + new string('-', 5998) + "\n");
        file.Raw("8 0 obj\n10\nendobj\n");
        file.Table(10, rows, "/Size 10 /Root 1 0 R");
        using var whole = PdfDocument.Open(file.Bytes);
        using var capped = PdfDocument.Open(new CappedReadSource(file.Bytes, 4096), options: null, ownsSource: true);

        var full = whole.Reader.GetScanMemo();
        var cut = capped.Reader.GetScanMemo();

        full.TryGetHeaderAt(rows[9], out var nine).Should().BeTrue();
        nine.Kind.Should().Be(ScannedPlaceKind.Clean);
        cut.WholeUntil.Should().Be(data);
        Headers(cut, 0, cut.WholeUntil).Should().Equal(Headers(full, 0, cut.WholeUntil));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void A_header_the_end_of_a_short_read_cuts_lies_past_where_the_memo_is_whole_and_is_served_as_the_bytes_give_it(int before)
    {
        // Read through a source that gives at most 4,096 bytes a read, object 4's header starts the given count of bytes
        // before the end of the scan's first window, its obj keyword ending past it, and its row lies 3 bytes past it. The
        // scan takes the window for the end of the data and cannot take the header, which may start as far back as where
        // object 3's value ends: the memo is whole only before there. The search near the row serves the header as the bytes
        // give it, a choice it cannot vouch for; object 5, past the end, is read at its row, and nothing is rebuilt (#52).
        const int Cap = 4096;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        rows[3] = file.Padding(3, (int)(Cap - before - file.Position - "3 0 obj\n()\nendobj\n".Length));
        var header = file.Object(4, "(four)");
        rows[4] = header + 3;
        rows[5] = file.Object(5, "(five)");
        file.Table(6, rows, "/Size 6 /Root 1 0 R");
        using var document = PdfDocument.Open(new CappedReadSource(file.Bytes, Cap), options: null, ownsSource: true);

        var four = document.GetObject(new PdfObjectId(4)).AsText();
        var five = document.GetObject(new PdfObjectId(5)).AsText();
        var memo = document.Reader.GetScanMemo();

        header.Should().Be(Cap - before);
        (four, five, document.WasRepaired).Should().Be(("four", "five", false));
        document.Diagnostics.Select(entry => (entry.Code, entry.Position)).Should().Equal((PdfDiagnosticCodes.XRefOffsetAdjusted, header));
        memo.IsWhole.Should().BeTrue();
        memo.WholeUntil.Should().BeInRange(rows[3] + 1, header);
        memo.TryGetHeaderAt(rows[3], out _).Should().BeTrue();
        new PdfValidator().Validate(document).Findings.Where(finding => finding.Location.Object == new PdfObjectId(4))
            .Select(finding => (finding.RuleId, finding.Severity)).Should().Equal((PdfValidationRuleIds.XRefEntryUnconfirmed, PdfValidationSeverity.Error));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(7)]
    public void A_header_that_follows_an_object_with_no_value_lies_past_where_the_memo_is_whole_wherever_a_short_read_cuts_it(int cut)
    {
        // Object 5's header has no value, and object 12's header follows it directly (#230), read through a source whose
        // reads end the given count of bytes into object 12's header: past its number, past its generation, or inside its obj
        // keyword. The scan takes the window for the end of the data, and object 5's value as ending where object 12's header
        // may start: the memo is whole only before it. The search near object 12's row, 3 bytes off, serves the header as the
        // bytes give it, a choice it cannot vouch for; object 7, past it, is read at its row, and nothing is rebuilt (#52).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        rows[3] = file.Padding(3, 4000);
        rows[5] = file.Position;
        file.Raw("5 0 obj\n");
        var twelve = file.Object(12, "(twelve)");
        rows[12] = twelve + 3;
        rows[7] = file.Object(7, "(after)");
        file.Table(13, rows, "/Size 13 /Root 1 0 R");
        using var document = PdfDocument.Open(new CappedReadSource(file.Bytes, (int)twelve + cut), options: null, ownsSource: true);

        var served = document.GetObject(new PdfObjectId(12)).AsText();
        var seven = document.GetObject(new PdfObjectId(7)).AsText();
        var memo = document.Reader.GetScanMemo();

        (served, seven, document.WasRepaired).Should().Be(("twelve", "after", false));
        (memo.IsWhole, memo.WholeUntil, memo.TryGetHeaderAt(twelve, out _)).Should().Be((true, twelve, false));
        new PdfValidator().Validate(document).Findings.Where(finding => finding.Location.Object == new PdfObjectId(12))
            .Select(finding => (finding.RuleId, finding.Severity)).Should().Equal((PdfValidationRuleIds.XRefEntryUnconfirmed, PdfValidationSeverity.Error));
    }

    [Theory]
    [InlineData("the sweep for comments, short of an end-of-file marker's tail", "twelve")]
    [InlineData("the sweep for comments, short and then empty", "twelve")]
    [InlineData("the scan's next piece, short of where its searches resume in it", "twelve")]
    [InlineData("the delimiting of an object its piece cuts", "inner")]
    [InlineData("the search for the end of a stream whose endstream is lost", "twelve")]
    public void A_header_past_a_read_of_the_scan_that_gives_fewer_bytes_than_asked_lies_past_where_the_memo_is_whole(string read, string served)
    {
        // Past the catalog and the page tree, a megabyte and more of comment lines, read through a source that gives every
        // read whole but one: the scan's sweep from the file across the comments, which ends just short of the end-of-file
        // marker that object 12's header follows on its line, or gives a hundred bytes and then none; or the scan's next
        // piece, which gives fewer bytes than the scan reads in it before where its searches resume; or the read again of a
        // stream whose dictionary its piece cuts, whose data holds object 12's header. Or, with no comment lines, the first
        // read of the search for the end of a stream that lost its endstream, which gives none. The window holds the end of
        // the data there (#52): the memo is whole only before where the page tree's value ends, and the search near object
        // 12's row, 3 bytes off, serves the header as the bytes give it, a choice it cannot vouch for, nothing rebuilt.
        const int Piece = 1 << 20;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        var pagesEnd = rows[2] + "2 0 obj\n".Length + Pages.Length;
        long twelve;
        long shortAt;
        int shortCount;

        if (read == "the search for the end of a stream whose endstream is lost")
        {
            file.Raw("5 0 obj\n<< /Length 10 >>\nstream\n");
            shortAt = file.Position;
            shortCount = 0;
            file.Raw("0123456789\nendobj\n");
            twelve = file.Object(12, "(twelve)");
        }
        else if (read == "the delimiting of an object its piece cuts")
        {
            PadWithComments(file, Piece - 20);
            shortAt = file.Position;
            shortCount = 30;
            file.Raw("9 0 obj\n<< /Length 24 >>\nstream\n");
            twelve = file.Position;
            file.Raw("12 0 obj (inner) endobj\n\nendstream\nendobj\n");
        }
        else if (read == "the sweep for comments, short of an end-of-file marker's tail")
        {
            PadWithComments(file, Piece - 96);
            var marker = file.Position;
            file.Raw("%%EOF" + new string(' ', 100));
            shortAt = pagesEnd;
            shortCount = (int)(marker + 3 - pagesEnd);
            twelve = file.Object(12, "(twelve)");
        }
        else if (read == "the scan's next piece, short of where its searches resume in it")
        {
            // The first piece ends in comment lines, and the next starts 71 bytes before its end: the 64 bytes the scan reads
            // before where its search for trailer resumes, 7 bytes before that end, the searches for obj and xref a few bytes
            // past it.
            PadWithComments(file, Piece + (Piece / 4));
            shortAt = Piece - 71;
            shortCount = 30;
            twelve = file.Object(12, "(twelve)");
        }
        else
        {
            PadWithComments(file, Piece + (Piece / 4));
            shortAt = pagesEnd;
            shortCount = 100;
            twelve = file.Object(12, "(twelve)");
        }

        rows[12] = twelve + 3;
        rows[7] = file.Object(7, "(after)");
        file.Table(13, rows, "/Size 13 /Root 1 0 R");
        var given = new HashSet<long>();
        var source = new ShortCountSource(file.Bytes, (offset, held) =>
            held <= shortCount || !given.Add(offset) ? held
            : offset == shortAt ? shortCount
            : offset == shortAt + shortCount && read == "the sweep for comments, short and then empty" ? 0
            : held);
        using var document = PdfDocument.Open(source, options: null, ownsSource: true);

        var twelveServed = document.GetObject(new PdfObjectId(12)).AsText();
        var seven = document.GetObject(new PdfObjectId(7)).AsText();
        var memo = document.Reader.GetScanMemo();

        (twelveServed, seven, document.WasRepaired).Should().Be((served, "after", false));
        (memo.IsWhole, memo.WholeUntil).Should().Be((true, pagesEnd));
        new PdfValidator().Validate(document).Findings.Where(finding => finding.Location.Object == new PdfObjectId(12))
            .Select(finding => (finding.RuleId, finding.Severity)).Should().Equal((PdfValidationRuleIds.XRefEntryUnconfirmed, PdfValidationSeverity.Error));
    }

    [Theory]
    [InlineData("a direct length")]
    [InlineData("an indirect length the value before the stream holds")]
    public void A_rebuild_s_scan_reads_nothing_at_a_stream_s_data_to_learn_where_a_load_ends_it_and_takes_serves_and_says_what_a_whole_read_does(
        string length)
    {
        // The file has no startxref: opening rebuilds the index, and no memo is made. Stream 5 lost its endstream, and while
        // the document opens the source gives no byte of a read that starts at its data, as a caller's may (#52): the first
        // read of the search for where a load ends that data past its length, 10, direct or held by object 8 just before
        // the stream. That search decides only how a memo counts places, and a rebuild's scan, which keeps none, reads
        // neither the length's object nor where a load ends the data (#361): opening asks nothing there, and the rebuild
        // takes, serves and says what one whose reads are all whole does.
        var file = new HandWrittenFile();
        file.Object(1, Catalog);
        file.Object(2, Pages);
        file.Raw(length == "a direct length" ? "5 0 obj\n<< /Length 10 >>\nstream\n" : "8 0 obj\n10\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n");
        var dataStart = file.Position;
        file.Raw("0123456789\nendobj\n");
        file.Object(12, "(twelve)");
        file.Raw("trailer\n<< /Size 13 /Root 1 0 R >>\n%%EOF\n");
        var bytes = file.Bytes;
        var opening = true;
        var source = new ShortCountSource(bytes, (offset, held) => opening && offset == dataStart ? 0 : held);
        using var emptied = PdfDocument.Open(source, options: null, ownsSource: true);
        opening = false;
        using var whole = PdfDocument.Open(bytes);

        source.ShortReads.Should().Be(0);

        foreach (var document in new[] { emptied, whole })
        {
            document.WasRepaired.Should().BeTrue();
            document.Catalog.Should().NotBeNull();
            document.GetObject(new PdfObjectId(12)).AsText().Should().Be("twelve");
            var five = document.GetObject(new PdfObjectId(5)).Should().BeOfType<PdfStream>().Subject;
            Encoding.Latin1.GetString(five.GetRawBytes().Span).Should().Be("0123456789");
        }

        emptied.Diagnostics.Should().Equal(whole.Diagnostics);
        Entries(emptied).Should().Equal(Entries(whole));
    }

    [Theory]
    [InlineData("a direct length")]
    [InlineData("an indirect length the value before the stream holds")]
    public void A_header_past_the_first_endstream_of_a_stream_s_data_is_held_as_taken_between_objects_wherever_a_search_read_short_ends_the_data(string length)
    {
        // Stream 5's length, 40 or the 30 object 8 holds, runs past its endstream and object 9's header, which follows it;
        // the length object 8 holds is read at the trailer keyword the data holds. The source gives the reads that start
        // within the data's first 13 bytes no more than 8 bytes each, as a caller's source may (#52): the search for where a
        // load ends the data, whose reads there each start a byte further, never holds that endstream whole, runs on to
        // object 9's header, and ends the data a byte past it. The scan's own piece holds the endstream: the memo counts
        // what the scan takes as taken in the data only up to there, as far as the scan reads the data as the bytes give it,
        // and holds object 9's header as taken between objects.
        var stream = length == "a direct length" ? "5 0 obj\n<< /Length 40 >>\nstream\n" : "8 0 obj\n30\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n";
        var text = stream + "abc trailer\nendstream x\n9 0 obj (nine) endobj\nendobj\n";
        var (file, at) = Sound(text, text.IndexOf("9 0 obj", StringComparison.Ordinal));
        var dataStart = at - text.IndexOf("9 0 obj", StringComparison.Ordinal) + stream.Length;
        var source = new ShortCountSource(file, (offset, held) => offset >= dataStart && offset < dataStart + 13 ? Math.Min(held, 8) : held);
        using var document = PdfDocument.Open(source, options: null, ownsSource: true);

        var memo = document.Reader.GetScanMemo();

        source.ShortReads.Should().BePositive();
        memo.TryGetHeaderAt(at, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(at, 9, 0, ScannedPlaceKind.Clean));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(1000)]
    public void A_stream_that_lost_its_end_at_the_end_of_what_a_source_holds_ends_the_memo_however_much_its_search_read(int extra)
    {
        // The source says it holds more bytes than it does, as a caller's may: a read past them gives none (#52). The last
        // object, a stream, lost its endstream and endobj, and its row lies 3 bytes past its header. The search for where its
        // data ends reads windows that each repeat a few bytes of the one before, more than the source says it holds past the
        // data's start, then meets one the source gives empty: the memo is whole only before where the page tree's value
        // ends, and the search near the row serves the stream, a choice it cannot vouch for.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        var pagesEnd = rows[2] + "2 0 obj\n".Length + Pages.Length;
        rows[5] = file.Position + 3;
        file.Raw("5 0 obj\n<< /Length 10 >>\nstream\n0123456789\n");
        file.Table(6, rows, "/Size 6 /Root 1 0 R");
        using var document = PdfDocument.Open(new OverlongSource(file.Bytes, extra), options: null, ownsSource: true);

        var stream = document.GetObject(new PdfObjectId(5));
        var memo = document.Reader.GetScanMemo();

        stream.Should().BeOfType<PdfStream>();
        (memo.IsWhole, memo.WholeUntil).Should().Be((true, pagesEnd));
        new PdfValidator().Validate(document).Findings.Where(finding => finding.Location.Object == new PdfObjectId(5))
            .Select(finding => (finding.RuleId, finding.Severity)).Should().Equal((PdfValidationRuleIds.XRefEntryUnconfirmed, PdfValidationSeverity.Error));
    }

    [Theory]
    [InlineData("a direct length", 0)]
    [InlineData("a direct length", 1)]
    [InlineData("an indirect length", 0)]
    [InlineData("an indirect length", 1)]
    public void A_stream_length_that_ends_its_data_where_the_longest_source_ends_or_past_it_leaves_the_rebuild_to_take_the_objects_past_it(
        string length, int past)
    {
        // The source says it holds long.MaxValue bytes, as many as a position can name, and holds a few hundred: its tail
        // holds no startxref, and the document is rebuilt from the scan. Stream 3's length, in its dictionary or in object 8
        // before it, ends its data exactly where the source says it ends, or a byte past there, ends computed with no
        // overflow: nothing is read past the source's end, and the scan reads on through the data, whose header it takes as
        // held in data, to the later definition of 3.
        var direct = length == "a direct length";
        var file = new HandWrittenFile();
        file.Object(1, Catalog);
        file.Object(2, Pages);

        // The length is written with as many digits as the largest integer, which places the data's start.
        var dataStart = file.Position + Opening(long.MaxValue).Length;
        file.Raw(Opening(long.MaxValue - dataStart + past) + "0123 ");
        var nine = file.Position;
        file.Raw("9 0 obj (nine) endobj\nendstream\nendobj\n");
        file.Object(3, "null");
        file.Raw("trailer\n<< /Size 10 /Root 1 0 R >>\n%%EOF\n");
        var bytes = file.Bytes;

        using var document = PdfDocument.Open(new OverlongSource(bytes, long.MaxValue - bytes.Length), options: null, ownsSource: true);
        var memo = document.Reader.GetScanMemo();

        document.WasRepaired.Should().BeTrue();
        document.Catalog.Should().NotBeNull();
        document.GetObject(new PdfObjectId(9)).AsText().Should().Be("nine");
        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        memo.TryGetHeaderAt(nine, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(nine, 9, 0, ScannedPlaceKind.Raw));

        string Opening(long value) => direct
            ? string.Create(CultureInfo.InvariantCulture, $"3 0 obj\n<< /Length {value} >>\nstream\n")
            : string.Create(CultureInfo.InvariantCulture, $"8 0 obj\n{value}\nendobj\n3 0 obj\n<< /Length 8 0 R >>\nstream\n");
    }

    [Theory]
    [InlineData("an indirect length that white space past a first read follows")]
    [InlineData("a direct length that white space past a first read follows")]
    [InlineData("a direct length that white space past a first read follows, the read on past it given empty")]
    [InlineData("a direct length that white space past a first read follows, its endstream glued to letters past a first read")]
    public void A_read_short_of_the_endstream_after_a_declared_length_leaves_the_data_unconfirmed_not_ended_at_an_endobj_it_holds(string length)
    {
        // An intact stream whose data holds an attached file's text, an endobj and then a header of object 9, whose own
        // definition follows the stream, its row 3 bytes past the header the data holds. The source gives every read whole
        // but one, the first where the scan reads past the declared length for an endstream. For the indirect length, which
        // the object before the stream holds, and whose stream crosses the end of the scan's first piece so that the scan
        // reads past the data's end from the source, it gives the first read at the keyword, past the white space a first
        // read holds, short within it. For the direct length, it gives that read short too; or the first read from the
        // data's end, on past the white space the scan's piece holds, empty; or, where the keyword is glued to more letters
        // than the read at it holds, the next read of the run empty, so that where the token ends is never read. The bytes do
        // not tell that an endstream follows, nor that none does, and the stream's data is read as the bytes give it, not
        // ended at the endobj it holds.
        // The header in the data is taken as such, and the search near the row serves object 9's own definition, which it
        // vouches for.
        const int Piece = 1 << 20;
        var direct = length.StartsWith("a direct length", StringComparison.Ordinal);
        var empty = length.EndsWith("given empty", StringComparison.Ordinal);
        var glued = length.Contains("glued", StringComparison.Ordinal);
        var data = "q\nendobj\n9 0 obj\n(inner nine)\nendobj\nQ" + (direct ? string.Empty : new string('Q', 200));
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };

        if (!direct)
        {
            PadWithComments(file, Piece - 200);
            rows[8] = file.Object(8, data.Length.ToString(CultureInfo.InvariantCulture));
        }

        var stream = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"4 0 obj\n<< /Length {(direct ? data.Length.ToString(CultureInfo.InvariantCulture) : "8 0 R")} >>\nstream\n{data}"));
        var inner = stream + Encoding.Latin1.GetString(file.Bytes[(int)stream..]).IndexOf("9 0 obj", StringComparison.Ordinal);
        var dataEnd = file.Position;
        var keyword = dataEnd + 80;
        file.Raw(new string(' ', 80) + "endstream" + (glued ? new string('X', 100) : string.Empty) + "\nendobj\n");
        rows[4] = stream;
        rows[9] = inner + 3;
        var outer = file.Object(9, "(outer nine)");
        file.Table(10, rows, "/Size 10 /Root 1 0 R");
        var given = new HashSet<long>();
        var cut = empty ? dataEnd : glued ? keyword + PdfObjectParser.EndObjLookahead : keyword;
        var source = new ShortCountSource(file.Bytes, (offset, held) => offset == cut && given.Add(offset) ? (empty || glued ? 0 : 4) : held);
        using var document = PdfDocument.Open(source, options: null, ownsSource: true);

        var nine = document.GetObject(new PdfObjectId(9)).AsText();
        var memo = document.Reader.GetScanMemo();

        (nine, source.ShortReads).Should().Be(("outer nine", 1));
        memo.TryGetHeaderAt(inner, out var header).Should().BeTrue();
        header.Kind.Should().Be(ScannedPlaceKind.Raw);
        memo.TryGetHeaderAt(outer, out var own).Should().BeTrue();
        own.Kind.Should().Be(ScannedPlaceKind.Clean);
        new PdfValidator().Validate(document).Findings.Where(finding => finding.Location.Object == new PdfObjectId(9))
            .Select(finding => (finding.RuleId, finding.Severity)).Should().Equal((PdfValidationRuleIds.XRefEntryShifted, PdfValidationSeverity.Warning));
    }

    [Fact]
    public void A_length_s_object_looked_for_at_a_trailer_keyword_near_the_end_of_the_file_is_read_with_no_byte_asked_past_it()
    {
        // Stream 5 lost its endstream, and its indirect length names object 8, which the file lacks: the scan looks for that
        // object at the first header or trailer keyword past the data's start, here the table's trailer keyword, which starts
        // fewer bytes before the end of the file than the scan reads of a length's object. It asks for no byte past the end,
        // which a source may refuse (#167). The data runs to the end of the file as the bytes give it, and a load with no
        // length ends it there, where no header follows: the table's xref keyword, which the scan took before that end, ends
        // it sooner, and is held as taken between objects.
        var (file, _) = Sound("5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\nendobj\n", 0);
        var written = Encoding.Latin1.GetString(file);
        var table = written.LastIndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        using var document = PdfDocument.Open(new StrictCountingSource(file), options: null, ownsSource: true);

        var memo = document.Reader.GetScanMemo();

        (file.Length - written.LastIndexOf("trailer", StringComparison.Ordinal)).Should().BeLessThan(PdfObjectParser.EndObjLookahead);
        (document.WasRepaired, memo.IsWhole).Should().Be((false, true));
        memo.TryGetXRefKeywordAt(table, out var kind).Should().BeTrue();
        kind.Should().Be(ScannedPlaceKind.Clean);
    }

    [Theory]
    [InlineData("a stream with no length whose data starts at the end of the file", 9)]
    [InlineData("a stream with a direct length whose data starts at the end of the file", 9)]
    [InlineData("a stream with a negative length whose data starts at the end of the file", 9)]
    [InlineData("a stream with an indirect length whose data starts at the end of the file", 9)]
    [InlineData("a stream whose length's object, before it, ends its data at the end of the file", 6)]
    [InlineData("an attached file whose direct length ends its data at the end of the file", 6)]
    [InlineData("an attached file whose length's object, before it, ends its data at the end of the file, a header in its data", 9)]
    public void Data_that_starts_or_that_its_length_ends_at_the_end_of_a_file_past_the_scan_s_first_piece_asks_the_source_for_no_read_of_nothing(
        string shape, int last)
    {
        // A file over a mebibyte with no table, rebuilt as it opens, whose last stream starts in the scan's first piece and
        // ends the file past it: its dictionary runs past that piece, and its stream keyword ends the file, a truncation,
        // whatever its length; or its data runs past it, and its length, direct or in object 5 before it, ends it exactly at
        // the end of the file. The source refuses a read of nothing, as a source may (#167): opening the file, rebuilding its
        // index and making the memo ask it for none, and take what they take through a source that holds the file in memory.
        const string Head = "%PDF-1.4\n1 0 obj\n" + Catalog + "\nendobj\n2 0 obj\n" + Pages + "\nendobj\ntrailer\n<< /Root 1 0 R >>\n";
        var filler = new string('a', 1_200_000);
        var text = shape switch
        {
            "a stream with no length whose data starts at the end of the file" => Truncated(string.Empty),
            "a stream with a direct length whose data starts at the end of the file" => Truncated("/Length 4 "),
            "a stream with a negative length whose data starts at the end of the file" => Truncated("/Length -4 "),
            "a stream with an indirect length whose data starts at the end of the file" => Truncated("/Length 8 0 R "),
            "a stream whose length's object, before it, ends its data at the end of the file" =>
                Head + Measured("5 0 obj LENGTH endobj\n6 0 obj\n<< /Length 5 0 R >>\nstream\n[[" + filler + "]]"),
            "an attached file whose direct length ends its data at the end of the file" =>
                Head + Measured("6 0 obj\n<< /Type /EmbeddedFile /Length LENGTH >>\nstream\n[[" + filler + "]]"),
            _ => Head + Measured("5 0 obj LENGTH endobj\n6 0 obj\n<< /Type /EmbeddedFile /Length 5 0 R >>\nstream\n[[9 0 obj (nine) endobj\n" + filler + "]]"),
        };
        var file = Encoding.Latin1.GetBytes(text);
        using var strict = PdfDocument.Open(new StrictCountingSource(file), options: null, ownsSource: true);
        using var held = PdfDocument.Open(file);

        var memo = strict.Reader.GetScanMemo();

        (strict.WasRepaired, file.Length > 1024 * 1024).Should().Be((true, true));
        Entries(strict).Should().Equal(Entries(held));
        Headers(memo, 0, long.MaxValue).Should().Equal(Headers(held.Reader.GetScanMemo(), 0, long.MaxValue)).And.Contain(header => header.Number == last);

        string Truncated(string length) =>
            Head + "%" + new string('c', (1024 * 1024) - 200 - Head.Length) + "\n9 0 obj\n<< " + length + "/F (" + new string('f', 2000) + ") >>\nstream\n";
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void A_length_that_ends_a_stream_s_data_where_white_space_runs_to_the_end_of_the_file_is_told_no_endstream_follows_from_the_bytes_the_scan_holds(int fromEnd)
    {
        // Object 8, the value before stream 5, holds a length that runs past object 4's header, the first its data holds, to
        // the file's last end of line, or to the one before its end-of-file marker: only white space, and that comment,
        // follow it to the end of the file, so no endstream does, and a load ends the data a byte past object 4's header.
        // The memo holds object 9's header, past there, as taken between objects. The scan's piece holds those last bytes:
        // the source, which would give none where the length ends, is not asked for them.
        static string Text(long length) => string.Create(
            CultureInfo.InvariantCulture,
            $"8 0 obj\n{length:D6}\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\n4 0 obj (four) endobj\n9 0 obj\n(nine)\nendobj\n");
        var (draft, dataStart) = Sound(Text(0), Text(0).IndexOf("0123456789", StringComparison.Ordinal));
        var text = Text(draft.Length - fromEnd - dataStart);
        var (file, at) = Sound(text, text.IndexOf("9 0 obj", StringComparison.Ordinal));
        var source = new ShortCountSource(file, (offset, held) => offset == file.Length - fromEnd ? 0 : held);
        using var document = PdfDocument.Open(source, options: null, ownsSource: true);

        var memo = document.Reader.GetScanMemo();

        (document.WasRepaired, source.ShortReads).Should().Be((false, 0));
        memo.TryGetHeaderAt(at, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(at, 9, 0, ScannedPlaceKind.Clean));
    }

    [Theory]
    [InlineData("past a direct length", "Clean")]
    [InlineData("past the first endstream of data the value before its stream ends", "Raw")]
    public void The_scan_reads_through_white_space_past_a_declared_length_or_an_endstream_with_its_own_reads_leaving_the_loads_theirs(string where, string kind)
    {
        // A hundred spaces, more than a first read holds, then an endobj. Past stream 5's direct length, which no endstream
        // follows: the scan reads through them to find that no endstream follows, and again to find that the endobj ends the
        // data where a load ends it, and object 9's header, past it, is held as taken between objects. Past the first
        // endstream of stream 5's data, which object 9's header holds before it, where object 8, the value before the stream,
        // ends that data before the header: the scan reads through them to find that the endobj ends the stream there, which
        // may be an intact one, and the header stays held as taken in its data. Each read counts with the scan's own reads
        // past declared lengths, and the loads' searches for endstream keep all they may read.
        var text = where == "past a direct length"
            ? "5 0 obj\n<< /Length 10 >>\nstream\n0123456789" + new string(' ', 100) + "\nendobj\n9 0 obj\n(nine)\nendobj\n"
            : "8 0 obj\n10\nendobj\n5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\n9 0 obj (nine) endobj\nendstream" + new string(' ', 100) + "endobj\n";
        var (file, at) = Sound(text, text.IndexOf("9 0 obj", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        var memo = document.Reader.GetScanMemo();

        memo.TryGetHeaderAt(at, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(at, 9, 0, Enum.Parse<ScannedPlaceKind>(kind)));
        document.Reader.EndStreamSearchBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "Clean")]
    public void Data_a_direct_length_no_endstream_follows_is_read_up_to_its_first_endstream_once_the_scan_s_reads_past_declared_lengths_reach_their_bound(
        int longer, string? kind)
    {
        // Forty stream headers of 36 bytes, each the first bytes of the data of the one before, whose direct lengths all end
        // where 64 spaces start, a letter after them, and no endstream anywhere; the data of the last holds objects 9 and 4,
        // and object 6 follows the letter. Each stream reads on through the spaces its first read cannot see past, 192 bytes:
        // once the last has read through them, the scan's reads past declared lengths come to 7,680 bytes, four times a file
        // of 1,920, their bound. The scan then reads the last's data, which it cannot confirm, as an intact stream's, up to
        // the end of the file, which holds no endstream, and takes nothing there (#361). One byte more of file, and it reads
        // that data as the bytes give it: the search for where a load ends it, which the memo's kinds alone hang on and which
        // has a budget of its own, ends it a byte past object 9's header, and objects 4 and 6 are held as taken between
        // objects.
        const int Headers = 40;
        const string Inner = "0123456789\n9 0 obj (nine) endobj\n4 0 obj (four) endobj\n";
        var text = new StringBuilder();

        for (var i = 0; i < Headers; i++)
        {
            text.Append(CultureInfo.InvariantCulture, $"8 0 obj<</Length {(36 * (Headers - i - 1)) + Inner.Length:D10}>>stream\n");
        }

        text.Append(Inner).Append(' ', 64).Append("x\n6 0 obj (six) endobj\n");
        var length = (192 * Headers / PdfFileReader.EndStreamSearchPasses) + longer;
        text.Append('%', length - Sound(text.ToString(), 0).File.Length - 1).Append('\n');
        var shape = text.ToString();
        var (file, four) = Sound(shape, shape.IndexOf("4 0 obj", StringComparison.Ordinal));
        var six = four + shape.IndexOf("6 0 obj", StringComparison.Ordinal) - shape.IndexOf("4 0 obj", StringComparison.Ordinal);
        using var document = PdfDocument.Open(file);

        var memo = document.Reader.GetScanMemo();

        file.Length.Should().Be(length);
        memo.TryGetHeaderAt(four, out var header).Should().Be(kind is not null);
        memo.TryGetHeaderAt(six, out var sixth).Should().Be(kind is not null);

        if (kind is not null)
        {
            header.Should().Be(new ScannedHeader(four, 4, 0, Enum.Parse<ScannedPlaceKind>(kind)));
            sixth.Should().Be(new ScannedHeader(six, 6, 0, Enum.Parse<ScannedPlaceKind>(kind)));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(400)]
    public void A_stream_no_endstream_follows_to_the_end_of_the_file_is_not_read_on_to_tell_it_from_an_intact_one(int spaces)
    {
        // Stream 5 starts 300 bytes before the scan's first piece ends, and no endstream follows it, 600,000 bytes on to the
        // end of the file. Object 8, the value before it, holds a length short of the header its data holds, in that piece,
        // or, past spaces, in the next, which no longer holds the bytes past that length: the memo holds that header as taken
        // between objects. With no endstream after the data, no intact stream can hold it, and the scan reads nothing to tell
        // one: past its first piece, it reads the file twice — once searching for the data's first endstream, once in its
        // next piece —, and the few bytes of the search for where a load ends the data.
        const int Piece = 1 << 20;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        PadWithComments(file, Piece - 300);
        rows[8] = file.Object(8, "5");
        file.Raw("5 0 obj\n<< /Length 8 0 R >>\nstream\n01234567\n" + new string(' ', spaces));
        var inner = file.Position;
        file.Raw("9 0 obj (nine) endobj\n" + new string('x', 600_000) + "\n");
        file.Table(9, rows, "/Size 9 /Root 1 0 R");
        var bytes = file.Bytes;
        var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);

        var before = source.BytesRead;
        var memo = document.Reader.GetScanMemo();
        var read = source.BytesRead - before;

        (inner > Piece).Should().Be(spaces > 0);
        memo.TryGetHeaderAt(inner, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(inner, 9, 0, ScannedPlaceKind.Clean));
        read.Should().BeLessThan(Piece + (2L * (bytes.Length - Piece)) + 4096);
    }

    [Fact]
    public void A_stream_s_indirect_length_is_looked_for_at_no_more_than_the_last_eight_values_the_scan_passed_however_many_it_passed()
    {
        // A thousand values end 300 bytes before the scan's first piece does. Stream 5, past that piece, names object 8, which
        // the file lacks, for its length, and its data holds a header: the scan looks for object 8 there among the last values
        // it passed, each read from the file, for they lie before the piece it holds. What it reads besides the file once
        // stays within a few kilobytes: the last eight values, not the thousand.
        const int Piece = 1 << 20;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        var values = string.Concat(Enumerable.Range(10, 1000).Select(number => string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n0\nendobj\n")));
        PadWithComments(file, Piece - 300 - values.Length);
        file.Raw(values);
        PadWithComments(file, Piece + 100);
        file.Raw("5 0 obj\n<< /Length 8 0 R >>\nstream\n0123 ");
        var inner = file.Position;
        file.Raw("9 0 obj (nine) endobj\nendstream\nendobj\n");
        file.Table(3, rows, "/Size 3 /Root 1 0 R");
        var bytes = file.Bytes;
        var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);

        var before = source.BytesRead;
        var memo = document.Reader.GetScanMemo();
        var read = source.BytesRead - before;

        memo.TryGetHeaderAt(inner, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(inner, 9, 0, ScannedPlaceKind.Raw));
        read.Should().BeLessThan(bytes.Length + 4096);
    }

    [Fact]
    public void A_search_for_a_stream_keyword_past_the_scan_s_piece_stops_within_one_of_its_largest_reads_past_the_keyword()
    {
        // Stream 5, as in the theory below, starts 300 bytes before the scan's first piece ends, its data holding first a line
        // of text, then the header of object 6, its length's object, with a length that leaves bytes before that header; a stream keyword
        // starts 300,000 bytes past the piece, before the data's first endstream. The scan reads the file past its piece for
        // that keyword in reads that grow, each twice the one before, up to 64 KiB, and stops at the read that holds it:
        // besides its two pieces, the search for the data's first endstream and a few short reads, it reads the stretch up to
        // the keyword and less than one of those largest reads past it.
        const int Piece = 1 << 20;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        PadWithComments(file, Piece - 300);
        file.Raw("5 0 obj\n<< /Length 6 0 R >>\nstream\ntext 1.4\n6 0 obj\n5\nendobj\n");
        var inner = file.Position;
        file.Raw("9 0 obj\n(inner)\nendobj\n" + new string('x', 300_000) + "\n");
        var keyword = file.Position;
        file.Raw("stream\n" + new string('x', 300_000) + "\nendstream\nendobj\n");
        file.Object(6, "49");
        file.Table(3, rows, "/Size 3 /Root 1 0 R");
        var bytes = file.Bytes;
        var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);

        var before = source.BytesRead;
        var memo = document.Reader.GetScanMemo();
        var read = source.BytesRead - before;

        memo.TryGetHeaderAt(inner, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(inner, 9, 0, ScannedPlaceKind.Clean));
        read.Should().BeLessThan(Piece + (2L * (bytes.Length - Piece)) + (keyword - Piece) + PdfFileReader.EndStreamSearchChunk + 4096);
    }

    [Fact]
    public void A_search_for_a_stream_keyword_ends_with_the_read_that_reaches_the_end_of_the_file()
    {
        // Object 5 lost its endstream, and the object that holds its length is never met: the scan awaits it to the end of
        // the file. Read through a source that gives at most three bytes fewer than the file holds a read, the scan's window
        // ends on object 7's stream keyword, which the file's bytes go on as streamabc, no keyword (#52). The wait ends with a
        // search for a keyword from object 7's header, 63 bytes before the end of the file, whose first read reaches that end
        // and finds none: the search reads no more, not even the last bytes again, as many as a keyword and the byte before.
        // Past them, only the three bytes of object 7's data are read, to tell whether they start an attached file.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        file.Raw("%" + new string('-', 5998) + "\n");
        rows[5] = file.Position;
        file.Raw("5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\n");
        file.Table(9, rows, "/Size 9 /Root 1 0 R");
        var seven = file.Position;
        file.Raw("7 0 obj\n<<" + new string(' ', 41) + ">>\nstreamabc");
        var bytes = file.Bytes;
        var offsets = new List<long>();
        var source = new ShortCountSource(bytes, (offset, held) =>
        {
            offsets.Add(offset);
            return bytes.Length - 3;
        });
        using var document = PdfDocument.Open(source, options: null, ownsSource: true);

        document.Reader.GetScanMemo();

        (seven, source.ShortReads).Should().Be((bytes.Length - 63, 1));
        offsets.Should().Contain(seven - 1).And.NotContain(offset => offset >= bytes.Length - "\nstream".Length && offset != bytes.Length - "abc".Length);
    }

    [Theory]
    [InlineData("no stream keyword, 600,000 bytes before its endstream", 0, "Raw")]
    [InlineData("no stream keyword, 600,000 bytes before its endstream, the word upstream past its length's object", 0, "Raw")]
    [InlineData("no stream keyword, 600,000 bytes before its endstream, a word that holds stream where the first read past the piece starts", 0, "Raw")]
    [InlineData("no stream keyword, its endstream a megabyte past the byte before its length's object", 0, "Raw")]
    [InlineData("no stream keyword, its endstream a megabyte past its length's object", 0, "Raw")]
    [InlineData("a stream keyword, 300,000 bytes on", 0, "Clean")]
    [InlineData("a stream keyword that ends the scan's piece", 0, "Clean")]
    [InlineData("a stream keyword, 300,000 bytes on, the first read past the piece given short", 0, "Raw")]
    [InlineData("a stream keyword, 300,000 bytes on, read whatever the scan's reads past declared lengths spent before it", 20, "Clean", 300, 403_308)]
    [InlineData("no stream keyword, a megabyte and more before its endstream", 0, "Raw")]
    [InlineData("no stream keyword, a megabyte and more before its endstream, the end of the file within a read past the megabyte", 0, "Raw", 2000)]
    [InlineData("a stream keyword in the megabyte's last bytes", 0, "Clean")]
    [InlineData("a stream keyword in the megabyte's last bytes", 20, "Clean")]
    [InlineData("a stream keyword in the megabyte's last bytes, past streams whose searches spent the memo's reads", 0, "Raw", 300, 50_000, 10)]
    [InlineData("a stream keyword in the megabyte's last bytes, past streams whose searches spent the memo's reads, in a longer file", 0, "Clean", 300, 400_000, 10)]
    [InlineData("a stream keyword in the megabyte's last bytes, past streams whose searches spent the memo's reads, the last read starting at the bound", 0, "Raw", 300, 154_681, 10)]
    [InlineData("a stream keyword in the megabyte's last bytes, past streams whose searches spent the memo's reads, the last read starting short of the bound", 0, "Clean", 300, 154_682, 10)]
    public void A_stream_whose_data_starts_with_its_length_s_object_is_told_from_an_intact_one_by_what_it_may_read_past_the_scan_s_piece(
        string past, int headers, string kind, int lead = 300, int padding = 0, int spelled = 0)
    {
        // Stream 5 starts 300 bytes before the scan's first piece ends, or 2,000. Its data holds a line of text, then the
        // header of object 6, its length's object, which holds the length of that line, then object 9's header; its first
        // endstream lies past the piece. Whether a stream keyword starts between object 6's header and that endstream tells an
        // intact stream, whose places the memo holds as taken in it, from one whose length ends its data where object 6's
        // header starts, which it holds as taken between objects: the scan reads the file past its piece for it, in reads
        // that grow, up to the first keyword. It searches a megabyte from the byte before object 6's header, all of a stretch
        // that ends there, and does not tell a longer stretch, a byte longer too, however near the end of the file its last
        // read comes, nor a keyword past the megabyte; one in the megabyte's last bytes is told. A word that holds stream
        // holds no keyword, upstream in object 9's string, or one whose stream starts where the first read past the piece
        // does, which that read does not tell; a keyword the piece ends with is told by the read past the piece, which
        // repeats it with the byte after. A read the source gives short leaves object 6's header Raw, as in an intact stream.
        // The scan's reads past declared lengths do not: stream headers before it, whose direct lengths end where one run of
        // 256 KiB of white space starts, each read that run, and 403,308 bytes of white space before the table set where those
        // reads, counted with these, reached four times the length of the file, just before the read that holds a keyword
        // 300,000 bytes on; but these reads have a budget of their own, and the scan makes that read, and the megabyte's,
        // whatever the reads past declared lengths spent (#361). That budget is spent by ten streams before, their headers
        // glued to a sign, each the first bytes of the data of the one before, that lost their endstream before 800,000 bytes
        // of text: the search for where a load ends each one's data crosses the text. With 50,000 bytes of white space before
        // the table, the reads for the megabyte, counted with those searches, reach four times the length of the file before
        // they reach its last bytes, and object 6's header is Raw, as in an intact stream; 400,000 bytes leave them enough.
        // With 154,681 bytes, the read that holds the keyword would start where those reads reach that bound: it is not made,
        // and object 6's header is Raw; a byte more, and it is.
        const int Piece = 1 << 20;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };

        for (var i = 0; i < headers; i++)
        {
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"8 0 obj<</Length {36 * (headers - i - 1):D10}>>stream\n"));
        }

        if (headers > 0)
        {
            file.Raw(new string(' ', 256 * 1024) + "x\n");
        }

        for (var i = 0; i < spelled; i++)
        {
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"+{20 + i} 0 obj\n<< /Length 5 >>\nstream\nabcdefgh\n"));
        }

        if (spelled > 0)
        {
            file.Raw(string.Concat(Enumerable.Repeat(new string('t', 99) + "\n", 8_000)) + "endstream\nendobj\n");
        }

        PadWithComments(file, Piece - lead);
        file.Raw("5 0 obj\n<< /Length 6 0 R >>\nstream\ntext 1.4\n");
        var lengthObject = file.Position;
        file.Raw("6 0 obj\n9\nendobj\n");
        file.Raw(past.Contains("upstream", StringComparison.Ordinal) ? "9 0 obj\n(upstream)\nendobj\n" : "9 0 obj\n(inner)\nendobj\n");

        if (past.EndsWith("where the first read past the piece starts", StringComparison.Ordinal))
        {
            file.Raw(new string('x', (int)(Piece - "\nstream".Length - file.Position)) + "stream");
        }

        file.Raw(past switch
        {
            _ when past.StartsWith("a stream keyword that ends", StringComparison.Ordinal) =>
                new string('x', (int)(Piece - "\nstream".Length - file.Position)) + "\nstream\n" + new string('x', 300_000),
            _ when past.StartsWith("a stream keyword in the megabyte's last bytes", StringComparison.Ordinal) =>
                new string('x', (int)(lengthObject - 1 + Piece - "\nstream\n".Length - file.Position)) + "\nstream\n" + new string('x', 1000),
            _ when past.StartsWith("a stream keyword", StringComparison.Ordinal) => new string('x', 300_000) + "\nstream\n" + new string('x', 300_000),
            _ when past.Contains("a megabyte past", StringComparison.Ordinal) =>
                new string('x', (int)(lengthObject - (past.Contains("the byte before", StringComparison.Ordinal) ? 1 : 0) + Piece - "\n".Length - file.Position)),
            _ when past.Contains("a megabyte", StringComparison.Ordinal) => new string('x', Piece + 1000),
            _ => new string('x', 600_000),
        });
        file.Raw("\nendstream\nendobj\n");
        file.Object(6, "49");
        file.Raw(new string(' ', padding));
        file.Table(3, rows, "/Size 3 /Root 1 0 R");
        var cut = past.EndsWith("given short", StringComparison.Ordinal);
        var source = new ShortCountSource(file.Bytes, (offset, held) => cut && offset == Piece - 7 ? 3 : held);
        using var document = PdfDocument.Open(source, options: null, ownsSource: true);

        var memo = document.Reader.GetScanMemo();

        (document.WasRepaired, source.ShortReads).Should().Be((false, cut ? 1 : 0));
        memo.TryGetHeaderAt(lengthObject, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(lengthObject, 6, 0, Enum.Parse<ScannedPlaceKind>(kind)));
    }

    [Theory]
    [InlineData(false, "Clean")]
    [InlineData(true, "Raw")]
    public void A_stream_whose_length_s_object_the_scan_awaits_into_a_later_piece_is_told_from_an_intact_one_by_the_file_s_bytes_before_that_piece(
        bool shortRead, string kind)
    {
        // Stream 5's indirect length names object 8, which comes last: the scan awaits it past stream 5's data, which a load
        // ends at the endobj after its ten bytes, and stops awaiting it at stream 4, past a megabyte of comment lines, in a
        // later piece, where stream 4 awaits its own. A stream keyword between where a load ends stream 5's data and where
        // stream 6's data starts tells that object 9's header, which the scan took in what it read as stream 5's data, lies
        // between objects: the scan searches for one from the byte before that end, which its piece no longer holds, reading
        // the file there, and finds stream 6's. A source that gives that read short leaves the header held as taken in the
        // data (#52).
        const int Piece = 1 << 20;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        file.Raw("5 0 obj\n<< /Length 8 0 R >>\nstream\n");
        var dataEnd = file.Position + "0123456789".Length;
        file.Raw("0123456789\nendobj\n");
        var nine = file.Object(9, "(nine)");
        file.Raw("6 0 obj\n<< /Length 3 >>\nstream\nabc\nendstream\nendobj\n");
        PadWithComments(file, file.Position + Piece);
        file.Raw("4 0 obj\n<< /Length 7 0 R >>\nstream\n0123\nendobj\n");
        file.Object(3, "(three)");
        file.Object(8, "10");
        file.Table(3, rows, "/Size 3 /Root 1 0 R");
        var source = new ShortCountSource(file.Bytes, (offset, held) => shortRead && offset == dataEnd - 1 ? 3 : held);
        using var document = PdfDocument.Open(source, options: null, ownsSource: true);

        var memo = document.Reader.GetScanMemo();

        (document.WasRepaired, source.ShortReads).Should().Be((false, shortRead ? 1 : 0));
        memo.TryGetHeaderAt(nine, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(nine, 9, 0, Enum.Parse<ScannedPlaceKind>(kind)));
    }

    [Theory]
    [InlineData(64, 0, "Clean")]
    [InlineData(63, 1, "Raw")]
    public void A_length_s_object_is_read_from_the_scan_s_piece_where_the_piece_holds_all_the_bytes_read_of_it_and_from_the_source_otherwise(
        int before, int shortReads, string kind)
    {
        // Stream 5's indirect length names object 9, whose header follows its ten bytes and starts 64 bytes before the scan's
        // first piece ends, as many as the scan reads of a length's object, or 63; the stream's endstream and endobj are lost,
        // an endstream in a string past it. Where the piece holds those bytes to the last, the scan reads object 9 from it, and
        // holds the header as taken between objects, as a load with the length it holds ends the data before it. Otherwise
        // it reads them from the source, which gives a read that starts at the header 12 bytes, short of its endobj: it then
        // takes no length, and the header stays held as taken in the data (#52).
        const int Piece = 1 << 20;
        const string Stream = "5 0 obj\n<< /Length 9 0 R >>\nstream\n0123456789\n";
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        PadWithComments(file, Piece - before - Stream.Length);
        file.Raw(Stream);
        var nine = file.Position;
        file.Raw("9 0 obj\n10\nendobj\n6 0 obj\n(endstream)\nendobj\n");
        file.Table(3, rows, "/Size 3 /Root 1 0 R");
        var source = new ShortCountSource(file.Bytes, (offset, held) => offset == nine ? 12 : held);
        using var document = PdfDocument.Open(source, options: null, ownsSource: true);

        var memo = document.Reader.GetScanMemo();

        (nine, document.WasRepaired, source.ShortReads).Should().Be((Piece - before, false, shortReads));
        memo.TryGetHeaderAt(nine, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(nine, 9, 0, Enum.Parse<ScannedPlaceKind>(kind)));
    }

    [Fact]
    public void An_xref_keyword_past_a_sweep_ahead_that_gives_fewer_bytes_than_asked_lies_past_where_the_memo_is_whole_and_its_section_is_taken_unconfirmed()
    {
        // Past the catalog and the page tree, a megabyte of comment lines, then the end-of-file marker, and on its line the
        // first table, which the update's /Prev names 2 bytes into. The source gives every read whole but the scan's sweep
        // ahead to the keyword, which ends just short of the marker's tail: the window holds the end of the data there (#52),
        // the memo is whole only before where the page tree's value ends, and the search near the offset tries the keyword
        // without the memo's word and takes the section, which the reader cannot vouch for.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        var pagesEnd = rows[2] + "2 0 obj\n".Length + Pages.Length;
        PadWithComments(file, (1 << 20) - 96);
        var marker = file.Position;
        file.Raw("%%EOF" + new string(' ', 100));
        var keyword = file.Table(3, rows, "/Size 3 /Root 1 0 R");
        var three = file.Object(3, "(three)");
        var update = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \n3 1\n{three:D10} 00000 n \ntrailer\n<< /Size 4 /Root 1 0 R /Prev {keyword + 2} >>\nstartxref\n{update}\n%%EOF\n"));
        var shortCount = (int)(marker + 3 - pagesEnd);
        var given = new HashSet<long>();
        var source = new ShortCountSource(file.Bytes, (offset, held) =>
            held > shortCount && offset == pagesEnd && given.Add(offset) ? shortCount : held);
        using var document = PdfDocument.Open(source, options: null, ownsSource: true);

        var section = document.Reader.Structure.Sections[1];
        var memo = document.Reader.GetScanMemo();

        (section.State, section.Offset).Should().Be((XRefSectionState.Relocated, keyword));
        document.GetObject(new PdfObjectId(3)).AsText().Should().Be("three");
        document.WasRepaired.Should().BeFalse();
        (memo.IsWhole, memo.WholeUntil, memo.TryGetXRefKeywordAt(keyword, out _)).Should().Be((true, pagesEnd, false));
        new PdfValidator().Validate(document).Findings.Where(finding => finding.RuleId.StartsWith("xref.section", StringComparison.Ordinal))
            .Select(finding => (finding.RuleId, finding.Severity, finding.Location.Position)).Should().Equal(
                (PdfValidationRuleIds.XRefSectionUnconfirmed, PdfValidationSeverity.Error, keyword));
    }

    [Fact]
    public void An_xref_keyword_where_the_scan_s_own_sweep_stands_with_the_sweep_ahead_is_told_as_the_scan_s_own_sweep_tells_it()
    {
        // Past the page tree, the end-of-file marker's line, a megabyte of comment lines, then an xref keyword, a trailer
        // keyword, a second xref keyword and the table. The source gives the first read at the page tree's end, the sweep
        // ahead's to the first keyword, short within the marker; the reads from there no further than a place on the
        // marker's line; and none from that place (#52). The sweep ahead, which the short read leaves in a comment, stops
        // there and hides the first keyword; the scan's own sweep, which reads the marker whole, stops there too for the
        // trailer keyword. Caught up with, the sweep ahead takes up the state the scan's own stands in: the second keyword
        // and the table's lie between objects. Under a bound of 4, the memo holds the second and fills at the table's; the
        // rebuild that object 3's row, outside the file, sets off scans the file again rather than replaying a memo that is
        // not whole.
        const int Piece = 1 << 20;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        var pagesEnd = rows[2] + "2 0 obj\n".Length + Pages.Length;
        var marker = file.Position;
        file.Raw("%%EOF" + new string(' ', 100) + "\n");
        PadWithComments(file, Piece + 100);
        file.Raw("xref\ntrailer\nxref\n");
        rows[3] = 9_999_999;
        var table = file.Table(4, rows, "/Size 4 /Root 1 0 R");
        var (cut, stop) = (marker + 3, marker + 90);
        var given = new HashSet<long>();
        var source = new ShortCountSource(file.Bytes, (offset, held) =>
            offset == pagesEnd && held > cut - offset && given.Add(offset) ? (int)(cut - offset)
            : offset >= pagesEnd && offset < stop ? (int)(stop - offset)
            : offset == stop ? 0
            : held);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 4 } };
        using var document = PdfDocument.Open(source, options, ownsSource: true);

        var memo = document.Reader.GetScanMemo();
        var shortReads = source.ShortReads;
        document.GetObject(new PdfObjectId(3));

        (memo.IsWhole, memo.FullAt, memo.WholeUntil).Should().Be((false, table, pagesEnd));
        source.ShortReads.Should().BeGreaterThan(shortReads, "the rebuild scans the file again");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void An_xref_keyword_the_end_of_a_short_read_cuts_lies_past_where_the_memo_is_whole_and_its_section_is_taken_unconfirmed(int before)
    {
        // Read through a source that gives at most 4,096 bytes a read, the first table's xref keyword starts the given count of
        // bytes before the end of the scan's first window, and the update's /Prev names the place 2 bytes into it. The scan
        // cannot take the keyword the window cuts, and its memo is whole only before where object 3's value ends: the search
        // near the offset tries the keyword without the memo's word, and takes the section, which the reader cannot vouch for
        // (#52).
        const int Cap = 4096;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        var padding = (int)(Cap - before - file.Position - "3 0 obj\n()\nendobj\n".Length);
        rows[3] = file.Padding(3, padding);
        var keyword = file.Table(4, rows, "/Size 4 /Root 1 0 R");
        var named = keyword + 2;
        var four = file.Object(4, "(four)");
        var update = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \n4 1\n{four:D10} 00000 n \ntrailer\n<< /Size 5 /Root 1 0 R /Prev {named} >>\nstartxref\n{update}\n%%EOF\n"));
        using var document = PdfDocument.Open(new CappedReadSource(file.Bytes, Cap), options: null, ownsSource: true);

        var section = document.Reader.Structure.Sections[1];

        keyword.Should().Be(Cap - before);
        (section.State, section.Offset).Should().Be((XRefSectionState.Relocated, keyword));
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture, $"The cross-reference section /Prev names at offset {named} was found 2 bytes before it."));
        document.GetObject(new PdfObjectId(3)).AsText().Should().Be(new string('x', padding));
        document.WasRepaired.Should().BeFalse();
        new PdfValidator().Validate(document).Findings.Where(finding => finding.RuleId.StartsWith("xref.section", StringComparison.Ordinal))
            .Select(finding => (finding.RuleId, finding.Severity, finding.Location.Position)).Should().Equal(
                (PdfValidationRuleIds.XRefSectionUnconfirmed, PdfValidationSeverity.Error, keyword));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_header_whose_walk_back_a_short_read_ends_is_not_taken_and_the_scan_goes_on_past_it(bool rebuildFirst)
    {
        // Read through a source that gives at most 1 MiB a read, each piece of the scan whole: past 800,000 bytes that hold no
        // header, object 5's number is followed by 300,000 spaces before its generation and its obj keyword, which lies in the
        // scan's second piece. Reading the header back from there asks for more than the source gives: the window comes back
        // short, the end of what the source holds there (#52), and the scan takes no header there but goes on past it, a memo
        // whole only before where the page tree's value ends, where the header it could not read may start. Object 6's row
        // lies 3 bytes past its header, and object 7's outside the file: the search near the row serves 6 as the bytes give
        // it, and the rebuild, run before the memo was made or after it, indexes 6 and not 5.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        file.Raw(new string('x', 800_000) + "\n");
        var five = file.Position;
        file.Raw("5" + new string(' ', 300_000) + "0 obj\n(five)\nendobj\n");
        var six = file.Object(6, "(six)");
        rows[6] = six + 3;
        rows[7] = 9_999_999;
        file.Table(8, rows, "/Size 8 /Root 1 0 R");
        using var document = PdfDocument.Open(new CappedReadSource(file.Bytes, 1 << 20), options: null, ownsSource: true);

        if (rebuildFirst)
        {
            document.GetObject(new PdfObjectId(7));
        }

        var served = document.GetObject(new PdfObjectId(6)).AsText();
        document.GetObject(new PdfObjectId(7));
        var memo = document.Reader.GetScanMemo();

        served.Should().Be("six");
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.XRefRebuilt);
        document.Diagnostics.Any(entry => entry.Code == PdfDiagnosticCodes.XRefOffsetAdjusted && entry.Position == six).Should().Be(!rebuildFirst);
        document.GetObject(new PdfObjectId(5)).Should().BeSameAs(PdfNull.Instance);
        document.Reader.Index.TryGet(5, out _).Should().BeFalse();
        document.Reader.Index.TryGet(6, out var entry).Should().BeTrue();
        document.Reader.PositionOf(entry).Should().Be(six);
        memo.IsWhole.Should().BeTrue();
        memo.WholeUntil.Should().Be(rows[2] + "2 0 obj\n".Length + Pages.Length);
        memo.TryGetHeaderAt(five, out _).Should().BeFalse();
        memo.TryGetHeaderAt(six, out _).Should().BeTrue();
    }

    [Fact]
    public void A_whole_memo_or_none_has_no_bound_to_report()
    {
        // A whole memo, and a document that has made none, report nothing, opened to throw on a guard or not.
        var file = Updated(out _);
        var options = new PdfReaderOptions { ThrowOnLimit = true };

        using var unmade = PdfDocument.Open(file, options);
        unmade.Reader.ReachScanMemoBound([0, long.MaxValue]);
        using var whole = PdfDocument.Open(file, options);
        whole.Reader.GetScanMemo().IsWhole.Should().BeTrue();
        whole.Reader.ReachScanMemoBound([0, long.MaxValue]);

        unmade.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries);
        whole.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries);
    }

    [Fact]
    public void A_whole_memo_keeps_the_trailers_in_trimmed_lists_and_one_past_its_bound_trims_none_of_the_lists_it_lets_go()
    {
        // Past the catalog and the page tree, ten thousand trailer keywords, then the file's cross-reference stream. The whole
        // memo keeps where they lie in lists that keep no more than a ninth more room than they use. Under a bound that objects
        // 1 and 2 and the keywords fill, the memo leaves the stream's header out and keeps no trailers: it makes no trimmed copy
        // of the lists it lets go, and so allocates about that copy less than the whole memo.
        const int Keywords = 10_000;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
        };
        file.Raw(string.Concat(Enumerable.Repeat("trailer\n", Keywords)));
        file.CrossReferenceStream(3, 4, rows, "/Root 1 0 R");
        var bytes = file.Bytes;
        var bounded = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = Keywords + 2 } };

        var wholeAllocated = Allocations.Least(() => PdfDocument.Open(bytes), document => document.Reader.GetScanMemo(), out var wholeDocument, out var whole);
        var cutAllocated = Allocations.Least(() => PdfDocument.Open(bytes, bounded), document => document.Reader.GetScanMemo(), out var cutDocument, out var cut);

        using (wholeDocument)
        using (cutDocument)
        {
            whole.IsWhole.Should().BeTrue();
            whole.Trailers!.Keywords.Should().HaveCount(Keywords);
            whole.Trailers.Streams.Should().ContainSingle();
            whole.Trailers.Keywords.Capacity.Should().BeLessThanOrEqualTo(whole.Trailers.Keywords.Count * 10 / 9);
            whole.Trailers.Streams.Capacity.Should().BeLessThanOrEqualTo(whole.Trailers.Streams.Count * 10 / 9);
            cut.IsWhole.Should().BeFalse();
            cut.Count.Should().Be(Keywords + 2);
            cut.Trailers.Should().BeNull();
            (wholeAllocated - cutAllocated).Should().BeGreaterThanOrEqualTo(
                Keywords * sizeof(long) / 2, "a memo past its bound trims no list it lets go ({0} bytes against {1})", wholeAllocated, cutAllocated);
        }
    }

    [Theory]
    [InlineData(1_000, 64)]
    [InlineData(100, 100)]
    public void A_memo_whose_places_end_filling_the_room_its_slots_grew_to_allocates_less_than_one_a_place_short_of_it(int bound, int places)
    {
        // The slots grow by doubling from the first 64, never past the bound, and are trimmed once the scan ends: a memo whose
        // places end filling the room the last growth made, the first 64 slots or the bound, keeps that array, where one a
        // place short of it allocates its trimmed copy too.
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = bound } };
        var fullFile = Alternating(places);
        var shortFile = Alternating(places - 1);

        var full = Allocations.Least(() => PdfDocument.Open(fullFile, options), document => document.Reader.GetScanMemo(), out var fullDocument, out var fullMemo);
        var oneShort = Allocations.Least(() => PdfDocument.Open(shortFile, options), document => document.Reader.GetScanMemo(), out var shortDocument, out var shortMemo);

        using (fullDocument)
        using (shortDocument)
        {
            (fullMemo.Count, fullMemo.IsWhole, shortMemo.Count, shortMemo.IsWhole).Should().Be((places, true, places - 1, true));
            full.Should().BeLessThan(oneShort, "a memo that ends with its room full trims nothing ({0} bytes against {1})", full, oneShort);
        }
    }

    [Theory]
    [InlineData("direct", 282_166, "Raw", "Raw")]
    [InlineData("direct", 282_167, "Clean", "Raw")]
    [InlineData("direct", 548_401, "Clean", "Raw")]
    [InlineData("direct", 548_402, "Clean", "Clean")]
    [InlineData("indirect, its object lost", 282_155, "Raw", "Raw")]
    [InlineData("indirect, its object lost", 282_156, "Clean", "Raw")]
    [InlineData("indirect, its object lost", 548_390, "Clean", "Raw")]
    [InlineData("indirect, its object lost", 548_391, "Clean", "Clean")]
    public void A_search_or_a_sweep_for_the_memo_s_kinds_that_would_start_at_its_bound_is_not_made(string length, int padding, string first, string second)
    {
        // A file with no cross-reference table: six streams that lost their endstream, the last five headers glued to a sign,
        // before a mebibyte of text and object 50, then a comment of padding. The scan for the memo searches each one's data
        // for where a load ends it, at object 50's header, which spends about 1,064,944 bytes of the reads made for the memo's
        // kinds each. The sixth's direct length ends its data where object 51's header starts, glued to a sign too: with
        // 282,166 bytes of padding, the file holds 1,331,180 bytes, and the five searches before the sixth spend four times
        // that, so that the sixth is not made, and object 51's header is held as taken in data; a byte more, and it is made,
        // and the header, where the length ends the data, is held as taken between objects, no byte swept before it (#361).
        // Object 50's header is held so where the sweep from object 51 to it, across the text, starts short of that bound,
        // past the sixth search, with 548,402 bytes of padding and not 548,401. Where the sixth's length's object is lost, its
        // search is made at the end of the file, and ends the data at object 50's header, held as taken between objects
        // where it is made, with 282,156 bytes and not 282,155; object 53's after it is held so where the sweep to it, from
        // object 50's header, starts short of that bound, with 548,391 bytes and not 548,390.
        var text = new StringBuilder("%PDF-1.7\n1 0 obj\n" + Catalog + "\nendobj\n2 0 obj\n" + Pages + "\nendobj\n");
        var direct = length == "direct";

        for (var i = 0; i < 6; i++)
        {
            text.Append(i == 0 ? "" : "+").Append(CultureInfo.InvariantCulture, $"{20 + i} 0 obj\n<< /Length {(direct || i < 5 ? "5" : "52 0 R")} >>\nstream\n")
                .Append(direct && i == 5 ? "abcd\n" : "abcdefgh\n");
        }

        text.Append(direct ? "+51 0 obj\n(fifty-one)\n" : string.Empty).Append('t', 1024 * 1024).Append("\n50 0 obj\n(fifty)\nendobj\n")
            .Append(direct ? string.Empty : "+53 0 obj\n(fifty-three)\n").Append('%').Append('p', padding).Append("\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");
        var written = text.ToString();
        var file = Encoding.Latin1.GetBytes(written);
        using var document = PdfDocument.Open(file);
        var fifty = written.IndexOf("\n50 0 obj", StringComparison.Ordinal) + 1;
        var places = direct ? (written.IndexOf("+51 0 obj", StringComparison.Ordinal), fifty) : (fifty, written.IndexOf("+53 0 obj", StringComparison.Ordinal));

        var memo = document.Reader.GetScanMemo();

        file.Length.Should().Be(1_331_180 + padding - (direct ? 282_166 : 282_155));
        Headers(memo, places.Item1, places.Item1 + 1).Should().ContainSingle().Which.Kind.Should().Be(Enum.Parse<ScannedPlaceKind>(first));
        Headers(memo, places.Item2, places.Item2 + 1).Should().ContainSingle().Which.Kind.Should().Be(Enum.Parse<ScannedPlaceKind>(second));
    }

    [Theory]
    [InlineData(400_000, false, "Raw")]
    [InlineData(600_000, true, "Clean")]
    public void Whether_a_keyword_starts_its_line_is_not_read_once_the_reads_for_the_memo_s_kinds_reach_their_bound(int padding, bool read, string kind)
    {
        // Six streams that lost their endstream before a mebibyte of text and object 50, the last five headers glued to a sign,
        // then a comment of padding, as above; the sixth's length's object is lost, and an xref keyword that starts its line
        // in its data, then object 9's header glued to a sign, end it for the memo's kinds where the bytes before the keyword
        // show the line. The search for where a load ends the sixth's data, made at the end of the file, brings the reads for
        // the memo's kinds past their bound with 400,000 bytes of padding: those bytes are not read, and object 9's header is
        // held as taken in the data. With 600,000, they are, and the header is held as taken between objects (#361, #372).
        var text = new StringBuilder("%PDF-1.7\n1 0 obj\n" + Catalog + "\nendobj\n2 0 obj\n" + Pages + "\nendobj\n");

        for (var i = 0; i < 6; i++)
        {
            text.Append(i == 0 ? "" : "+").Append(CultureInfo.InvariantCulture, $"{20 + i} 0 obj\n<< /Length {(i < 5 ? "5" : "52 0 R")} >>\nstream\n")
                .Append("abcdefgh\n");
        }

        text.Append("xref\n+9 0 obj\n(nine)\n").Append('t', 1024 * 1024).Append("\n50 0 obj\n(fifty)\nendobj\n").Append('%').Append('p', padding)
            .Append("\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");
        var written = text.ToString();
        var keyword = written.IndexOf("xref\n+9", StringComparison.Ordinal);
        var reads = new List<long>();
        using var source = new RecordingSource(Encoding.Latin1.GetBytes(written), reads);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        reads.Clear();

        var memo = document.Reader.GetScanMemo();

        reads.Contains(keyword - PdfObjectParser.EndObjLookahead).Should().Be(read);
        Headers(memo, keyword + "xref\n".Length, keyword + "xref\n".Length + 1).Should().ContainSingle().Which.Kind.Should().Be(Enum.Parse<ScannedPlaceKind>(kind));
    }

    [Theory]
    [InlineData(127, "Clean")]
    [InlineData(128, "Raw")]
    public void Whether_a_trailer_keyword_starts_its_line_is_read_only_short_of_the_bound_of_the_reads_for_the_memo_s_kinds(int padding, string kind)
    {
        // Stream 5 lost its endstream and its length's object; fifty trailer keywords in its data do not start their lines, and
        // a last one does, before object 9's header, glued to a sign, and object 10's, where a load ends the data. Each is read
        // back for whether it starts its line, 64 bytes counted with the reads made for the memo's kinds, and a comment of
        // padding past them sets their bound, four times the file. With 127 bytes, the reads for the fifty reach exactly the
        // bound: the last keyword is not read, the data ends at object 10's header, which is held as taken between objects,
        // no byte swept before it. With 128, they stop a byte short of it: the last keyword is read, the data ends there, and
        // the reads, past their bound, sweep nothing up to object 10's header, which stays held as taken in the data (#372).
        var text = "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\n" + string.Concat(Enumerable.Repeat("a trailer ", 50)) +
            "\ntrailer\n+9 0 obj\n(nine)\n10 0 obj\n(ten)\nendobj\n%" + new string('p', padding) + "\n";
        var (file, at) = Sound(text, text.IndexOf("10 0 obj", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);
        var memo = document.Reader.GetScanMemo();

        file.Length.Should().Be(828 + padding);
        memo.TryGetHeaderAt(at, out var header).Should().BeTrue();
        header.Kind.Should().Be(Enum.Parse<ScannedPlaceKind>(kind));
    }

    [Theory]
    [InlineData(100, 1, "Raw")]
    [InlineData(99, 2, "Clean")]
    public void Whether_a_place_lies_on_the_line_of_a_data_s_first_endstream_is_read_only_short_of_the_bound_of_the_reads_for_the_memo_s_kinds(
        int run, int padding, string kind)
    {
        // As above, 151 trailer keywords that do not start their lines spend most of what the reads made for the memo's kinds
        // may read, once attached file 15 ends the wait for stream 5's length's object. The objects after it spend some more,
        // each read once: stream 16, whose length's object, met on the line of its first endstream, ends the wait for it;
        // attached file 20, its length's object lost, which file 24 ends the wait for; past where stream 25's direct length
        // ends its data, two xref keywords on a line, told by a sweep ahead; stream 27, whose length's object, met past two
        // objects after its endstream, ends its data short of it; stream 32, whose length's object, met after stream 35 in
        // its data, ends that data short of where stream 35's starts, at object 36's header; stream 37, whose length's
        // object ends its data within a run of xref keywords, the same distance apart; stream 40, whose length's object ends
        // its data short of where stream 42's starts, object 42's header written again there. Streams 6 and 13 lost their
        // endstream, their direct lengths ending their data before an endobj, and a comment holds each one's first endstream:
        // the memo reads, for each place past it, whether it lies on that line, once for each line. Objects 3 and 7 lie on
        // stream 6's line, a run of letters past them, and objects 11 and 12 past it; object 9 on the line after stream 13's.
        // With a run of 100 letters and a comment of one byte of padding at the end, the reads reach exactly their bound
        // before object 9's line is read: it is taken to lie on the comment's line, and held as taken in the data; with 99
        // letters and two bytes, they stop a byte short of it, the line is read, and object 9's header held as taken between
        // objects (#372).
        var text = "5 0 obj\n<< /Length 8 0 R >>\nstream\n0123456789\n" + string.Concat(Enumerable.Repeat("a trailer ", 151)) + "\n" +
            "15 0 obj\n<< /Type /EmbeddedFile >>\nstream\nabc\nendstream\nendobj\n" +
            "16 0 obj\n<< /Length 17 0 R >>\nstream\n0123456789\nendobj\n18 0 obj (x) endobj\nendstream 17 0 obj\n10\nendobj 19 0 obj (y) endobj\n" +
            "20 0 obj\n<< /Type /EmbeddedFile /Length 21 0 R >>\nstream\n22 0 obj (x) endobj\nendstream 23 0 obj (y) endobj\n" +
            "24 0 obj\n<< /Type /EmbeddedFile >>\nstream\nabc\nendstream\nendobj\n" +
            "25 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n26 0 obj (x) endobj\n" + new string('a', 100) + " xref xref\nendstream\n" +
            "27 0 obj\n<< /Length 28 0 R >>\nstream\n0123456789\nendobj\n29 0 obj (x) endobj\nendstream\nendobj\n30 0 obj (a) endobj\n31 0 obj (b) endobj\n" +
            "28 0 obj\n10\nendobj\n" +
            "32 0 obj\n<< /Length 33 0 R >>\nstream\n0123456789\nendobj\n34 0 obj (x) endobj\n35 0 obj\n<< >>\nstream\n36 0 obj (z) endobj\nendstream\nendobj\n" +
            "33 0 obj\n10\nendobj\n" +
            "37 0 obj\n<< /Length 38 0 R >>\nstream\n+39 0 obj (x) endobj\nxref\nxref\nxref\nxref\n38 0 obj\n31\nendobj\n" +
            "40 0 obj\n<< /Length 43 0 R >>\nstream\n41 0 obj (x) endobj\n42 0 obj\n<< >>\nstream\n42 0 obj (z) endobj\nendstream\nendobj\n43 0 obj\n10\nendobj\n" +
            "6 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n4 0 obj (four) endobj\n" +
            "% endstream 3 0 obj (three) endobj 7 0 obj (seven) endobj " + new string('x', run) + "\n11 0 obj (eleven) endobj\n12 0 obj (twelve) endobj\n" +
            "13 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendobj\n14 0 obj (fourteen) endobj\n% endstream\n9 0 obj (nine) endobj\n%" +
            new string('p', padding) + "\n";
        var (file, at) = Sound(text, text.IndexOf("9 0 obj (nine)", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);
        var memo = document.Reader.GetScanMemo();

        file.Length.Should().Be(3_171 + run + padding);
        memo.TryGetHeaderAt(at, out var header).Should().BeTrue();
        header.Kind.Should().Be(Enum.Parse<ScannedPlaceKind>(kind));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_attached_file_whose_length_s_object_before_it_the_spent_search_does_not_confirm_keeps_its_places_up_to_where_that_length_ends_it(bool spent)
    {
        // An attached PDF file, object 6, whose length object 30 holds right before it, an endstream following that length: the
        // search confirms it, and the memo takes no place in its data. Five attached files before it, whose length's object
        // the file lacks, each have the search read to the end of the file, past a comment of padding: they spend its budget,
        // four times the file, and object 6 is not confirmed. The memo then learns from object 30 where its data ends, and
        // holds every place in it as taken there — object 4, past the file's own stream and the data's first endstream,
        // among them (#363, #366).
        const string Data = "%PDF-1.4\n1 0 obj << /Length 3 >> stream\nabc\nendstream endobj\n4 0 obj (inner) endobj\n%%EOF\n";
        var text = new StringBuilder();

        for (var number = 20; spent && number < 25; number++)
        {
            text.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /Type /EmbeddedFile /Length 99 0 R >>\nstream\nxyz\nendstream\nendobj\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"30 0 obj\n{Data.Length}\nendobj\n6 0 obj\n<< /Type /EmbeddedFile /Length 30 0 R >>\nstream\n")
            .Append(Data).Append("endstream\nendobj\n%").Append('p', 200_000).Append('\n');
        var written = text.ToString();
        var (file, four) = Sound(written, written.IndexOf("4 0 obj (inner)", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        var memo = document.Reader.GetScanMemo();

        memo.TryGetHeaderAt(four, out var header).Should().Be(spent);
        header.Kind.Should().Be(spent ? ScannedPlaceKind.Raw : default);
    }

    [Fact]
    public void A_memo_made_again_once_the_source_failed_its_scan_takes_its_places_as_one_the_source_never_failed()
    {
        // Three streams that lost their endstream, the second and third headers glued to a sign, before a mebibyte of text and
        // object 50: the scan for the memo searches each one's data for where a load ends it, at object 50's header, across
        // the mebibyte, which spends about three times the file of the reads made for the memo's kinds, and holds object 50's
        // header as taken between objects. The source fails the scan's last read, once; the memo asked for again scans the
        // file again, its reads counted from none, and holds each place as a memo the source never failed does (#361).
        var pdf = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = pdf.Object(1, Catalog), [2] = pdf.Object(2, Pages) };
        rows[20] = pdf.Position;
        pdf.Raw("20 0 obj\n<< /Length 5 >>\nstream\nabcdefgh\n+21 0 obj\n<< /Length 5 >>\nstream\nabcdefgh\n");
        pdf.Raw("+22 0 obj\n<< /Length 5 >>\nstream\nabcdefgh\n" + new string('t', 1024 * 1024) + "\n");
        rows[50] = pdf.Object(50, "(fifty)");
        pdf.Table(51, rows, "/Size 51 /Root 1 0 R");
        var file = pdf.Bytes;
        using var sound = PdfDocument.Open(file);
        var fifty = rows[50];
        var reads = new List<long>();
        using (var recording = new RecordingSource(file, reads))
        using (var recorded = PdfDocument.Open(recording, options: null, ownsSource: false))
        {
            reads.Clear();
            _ = recorded.Reader.GetScanMemo();
        }

        using var source = new FailingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        source.FailAt = reads[^1];

        FluentActions.Invoking(() => document.Reader.GetScanMemo()).Should().Throw<IOException>();
        var memo = document.Reader.GetScanMemo();

        Headers(memo, 0, long.MaxValue).Should().Equal(Headers(sound.Reader.GetScanMemo(), 0, long.MaxValue));
        Headers(memo, fifty, fifty + 1).Should().ContainSingle().Which.Kind.Should().Be(ScannedPlaceKind.Clean);
    }

    [Fact]
    public void A_rebuild_the_source_fails_past_the_memo_s_replay_leaves_the_memo_for_the_rebuild_run_again_to_take()
    {
        // The memo is asked for first; the source fails the rebuild that replays it as it merges the trailer: what the rebuild
        // reported is withdrawn, and the read that runs the rebuild again takes the same memo, with the index, the objects and
        // the diagnostics of a rebuild the source never failed, which scanned the file.
        var file = Updated(out _);
        var trailer = Encoding.Latin1.GetString(file).LastIndexOf("trailer", StringComparison.Ordinal) + "trailer".Length;
        using var source = new FailingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var memo = document.Reader.GetScanMemo();
        source.FailAt = trailer;

        FluentActions.Invoking(() => document.GetObject(new PdfObjectId(4))).Should().Throw<IOException>();
        document.GetObject(new PdfObjectId(4));

        using var sound = PdfDocument.Open(file);
        sound.GetObject(new PdfObjectId(4));

        document.Reader.GetScanMemo().Should().BeSameAs(memo);
        memo.IsWhole.Should().BeTrue();
        document.Diagnostics.Should().Equal(sound.Diagnostics);
        Entries(document).Should().Equal(Entries(sound));
        document.GetObject(new PdfObjectId(3)).AsText().Should().Be("three, updated");
    }

    [Fact]
    public void Headers_repeated_at_the_longest_distance_a_run_holds_take_no_more_room_than_at_a_distance_a_byte_shorter()
    {
        // Object 7 defined a hundred times, each a stream whose direct length an endstream confirms, so that the scan reads
        // none of its data: a file of 200 GB of which the source holds a few hundred bytes. A distance as long as an int holds
        // is the longest a run holds: the headers are held as one run there, as at a distance a byte shorter, and making the
        // memo allocates as much at either.
        const int Repeats = 100;
        var longest = Allocations.Least(() => RepeatedStreams(int.MaxValue, Repeats), document => document.Reader.GetScanMemo(), out var atLongest, out var memo);
        var shorter = Allocations.Least(() => RepeatedStreams(int.MaxValue - 1, Repeats), document => document.Reader.GetScanMemo(), out var atShorter, out _);

        using (atLongest)
        using (atShorter)
        {
            var positions = Headers(memo, 0, long.MaxValue).Where(header => header.Number == 7).Select(header => header.Position).ToList();
            positions.Should().Equal(Enumerable.Range(0, Repeats).Select(i => positions[0] + ((long)i * int.MaxValue)));
            memo.IsWhole.Should().BeTrue();
            longest.Should().BeLessThan(shorter + 1024, "the headers are held as one run at either distance ({0} against {1} bytes)", longest, shorter);
        }
    }

    [Fact]
    public void Places_of_one_header_at_one_distance_are_read_back_one_by_one()
    {
        var builder = new ScanMemo.Builder(100);

        foreach (var at in new long[] { 100, 108, 116, 124 })
        {
            builder.TryAddHeader(at, 7, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        }

        builder.TryAddHeader(200, 8, 65_535, ScannedPlaceKind.Raw, floor: 0).Should().BeTrue();
        var memo = builder.Complete(new ScannedTrailers());

        memo.Count.Should().Be(5);
        memo.IsWhole.Should().BeTrue();
        memo.WholeUntil.Should().Be(long.MaxValue);
        memo.InScanOrder.Should().BeTrue();
        memo.Trailers.Should().NotBeNull();
        Headers(memo, 0, long.MaxValue).Should().Equal(
            Header(100, 7), Header(108, 7), Header(116, 7), Header(124, 7), new ScannedHeader(200, 8, 65_535, ScannedPlaceKind.Raw));
        Headers(memo, 110, 125).Should().Equal(Header(116, 7), Header(124, 7));
        Headers(memo, 108, 109).Should().Equal(Header(108, 7));
        Headers(memo, 101, 116).Should().Equal(Header(108, 7));
        Headers(memo, 125, 200).Should().BeEmpty();

        foreach (var at in new long[] { 100, 108, 116, 124 })
        {
            memo.TryGetHeaderAt(at, out var header).Should().BeTrue();
            header.Should().Be(Header(at, 7));
        }

        memo.TryGetHeaderAt(99, out _).Should().BeFalse();
        memo.TryGetHeaderAt(112, out _).Should().BeFalse();
        memo.TryGetHeaderAt(132, out _).Should().BeFalse();
        memo.TryGetHeaderAt(200, out var last).Should().BeTrue();
        last.Should().Be(new ScannedHeader(200, 8, 65_535, ScannedPlaceKind.Raw));
    }

    [Theory]
    [InlineData("another number")]
    [InlineData("another generation")]
    [InlineData("another kind")]
    [InlineData("another distance")]
    [InlineData("a distance past what an int holds")]
    public void Places_that_differ_from_a_run_are_held_apart_from_it(string shape)
    {
        var builder = new ScanMemo.Builder(100);
        var stride = shape == "a distance past what an int holds" ? 3_000_000_000L : 8;
        var third = shape switch
        {
            "another number" => new ScannedHeader(2 * stride + 0, 9, 0, ScannedPlaceKind.Clean),
            "another generation" => new ScannedHeader(2 * stride, 7, 1, ScannedPlaceKind.Clean),
            "another kind" => new ScannedHeader(2 * stride, 7, 0, ScannedPlaceKind.Raw),
            "another distance" => new ScannedHeader(2 * stride + 1, 7, 0, ScannedPlaceKind.Clean),
            _ => new ScannedHeader(2 * stride, 7, 0, ScannedPlaceKind.Clean),
        };
        ScannedHeader[] places = [Header(0, 7), Header(stride, 7), third, Header(third.Position + 5, 7)];

        foreach (var place in places)
        {
            builder.TryAddHeader(place.Position, place.Number, place.Generation, place.Kind, floor: 0).Should().BeTrue();
        }

        var memo = builder.Complete(new ScannedTrailers());
        Headers(memo, 0, long.MaxValue).Should().Equal(places);

        foreach (var place in places)
        {
            memo.TryGetHeaderAt(place.Position, out var header).Should().BeTrue();
            header.Should().Be(place);
        }

        memo.TryGetHeaderAt(stride / 2, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(50L)]
    [InlineData(100L)]
    [InlineData(104L)]
    [InlineData(116L)]
    [InlineData(124L)]
    [InlineData(140L)]
    [InlineData(148L)]
    public void A_place_taken_before_the_last_is_put_in_its_place_and_splits_the_run_it_falls_within(long position)
    {
        // A run of seven places of 7, from 100 to 148, eight bytes apart, then a header of 9 the scan took after it.
        var builder = new ScanMemo.Builder(100);
        var run = new List<ScannedHeader>();

        for (var at = 100L; at <= 148; at += 8)
        {
            builder.TryAddHeader(at, 7, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
            run.Add(Header(at, 7));
        }

        builder.TryAddHeader(position, 9, 0, ScannedPlaceKind.Raw, floor: 0).Should().BeTrue();
        var memo = builder.Complete(new ScannedTrailers());

        var inserted = new ScannedHeader(position, 9, 0, ScannedPlaceKind.Raw);
        var expected = run.Where(header => header.Position <= position).Append(inserted).Concat(run.Where(header => header.Position > position)).ToList();
        memo.InScanOrder.Should().BeFalse();
        memo.Count.Should().Be(8);
        Headers(memo, 0, long.MaxValue).Should().Equal(expected);
        Headers(memo, position + 1, long.MaxValue).Should().Equal(run.Where(header => header.Position > position));
        Headers(memo, 0, position).Should().Equal(run.Where(header => header.Position < position));

        foreach (var header in run.Where(header => header.Position != position))
        {
            memo.TryGetHeaderAt(header.Position, out var found).Should().BeTrue();
            found.Should().Be(header);
        }

        memo.TryGetHeaderAt(position, out var at9).Should().BeTrue();
        at9.Should().Be(inserted);
    }

    [Theory]
    [InlineData(50L, 104L, false)]
    [InlineData(104L, 141L, false)]
    [InlineData(100L, 148L, false)]
    [InlineData(100L, 149L, false)]
    [InlineData(90L, 165L, false)]
    [InlineData(116L, 200L, false)]
    [InlineData(200L, 300L, false)]
    [InlineData(150L, 50L, false)]
    [InlineData(50L, 104L, true)]
    [InlineData(100L, 148L, true)]
    [InlineData(100L, 149L, true)]
    [InlineData(90L, 165L, true)]
    [InlineData(116L, 200L, true)]
    public void Places_held_as_taken_between_two_bounds_keep_their_kind_in_a_run_a_bound_cuts(long from, long to, bool inData)
    {
        // A run of seven places of 7, from 100 to 148, eight bytes apart, then a keyword at 160 and a header of 9 at 170, all
        // taken as the bytes give them, or all between objects: those from the first bound on and before the second are held
        // as taken the other way — slot by slot as the scan holds them between objects, or as an attached file's intact data
        // holds them in it —, but for the places of a run that also holds places on the other side of a bound.
        var (taken, held) = inData ? (ScannedPlaceKind.Clean, ScannedPlaceKind.Raw) : (ScannedPlaceKind.Raw, ScannedPlaceKind.Clean);
        var builder = new ScanMemo.Builder(100);
        var headers = new List<ScannedHeader>();

        for (var at = 100L; at <= 148; at += 8)
        {
            builder.TryAddHeader(at, 7, 0, taken, floor: 0).Should().BeTrue();
            headers.Add(new ScannedHeader(at, 7, 0, taken));
        }

        builder.TryAddXRefKeyword(160, taken, floor: 0).Should().BeTrue();
        builder.TryAddHeader(170, 9, 0, taken, floor: 0).Should().BeTrue();
        headers.Add(new ScannedHeader(170, 9, 0, taken));

        if (inData)
        {
            builder.TakeRaw(from, to);
        }
        else
        {
            for (var index = builder.IndexAtOrAfter(from); builder.TryGetPlaces(index, to, out var first, out var last, out var slots); index += slots)
            {
                if (first >= 0 && last < to)
                {
                    builder.ClearRaw(index);
                }
            }
        }

        var memo = builder.Complete(new ScannedTrailers());

        var runWithin = from <= 100 && to > 148;
        var expected = headers
            .Select(header => header.Position >= from && header.Position < to && (header.Number != 7 || runWithin) ? header with { Kind = held } : header)
            .ToList();
        memo.Count.Should().Be(9);
        Headers(memo, 0, long.MaxValue).Should().Equal(expected);
        Headers(memo, 117, 165).Should().Equal(expected.Where(header => header.Position is >= 117 and < 165));

        foreach (var header in expected)
        {
            memo.TryGetHeaderAt(header.Position, out var found).Should().BeTrue();
            found.Should().Be(header);
        }

        memo.TryGetHeaderAt(112, out _).Should().BeFalse();
        memo.TryGetXRefKeywordAt(160, out var keyword).Should().BeTrue();
        keyword.Should().Be(160 >= from && 160 < to ? held : taken);
    }

    [Fact]
    public void Xref_keywords_are_found_where_they_start_and_are_no_headers()
    {
        var builder = new ScanMemo.Builder(100);
        builder.TryAddXRefKeyword(10, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        builder.TryAddXRefKeyword(20, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        builder.TryAddXRefKeyword(30, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        builder.TryAddHeader(40, 5, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        builder.TryAddXRefKeyword(50, ScannedPlaceKind.Raw, floor: 0).Should().BeTrue();

        var memo = builder.Complete(new ScannedTrailers());

        Headers(memo, 0, long.MaxValue).Should().Equal(Header(40, 5));
        Headers(memo, 15, long.MaxValue).Should().Equal(Header(40, 5));
        memo.TryGetXRefKeywordAt(20, out var kind).Should().BeTrue();
        kind.Should().Be(ScannedPlaceKind.Clean);
        memo.TryGetXRefKeywordAt(50, out kind).Should().BeTrue();
        kind.Should().Be(ScannedPlaceKind.Raw);
        memo.TryGetXRefKeywordAt(25, out _).Should().BeFalse();
        memo.TryGetXRefKeywordAt(40, out _).Should().BeFalse();
        memo.TryGetHeaderAt(20, out _).Should().BeFalse();
        memo.TryGetHeaderAt(30, out _).Should().BeFalse();
    }

    [Fact]
    public void A_memo_takes_no_place_past_its_bound_and_is_whole_only_before_the_first_it_left_out()
    {
        var builder = new ScanMemo.Builder(3);
        builder.TryAddHeader(10, 1, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        builder.TryAddTrailer(20, floor: 15).Should().BeTrue();
        builder.TryAddXRefKeyword(30, ScannedPlaceKind.Clean, floor: 25).Should().BeTrue();
        builder.IsWhole.Should().BeTrue();

        builder.TryAddHeader(40, 2, 0, ScannedPlaceKind.Clean, floor: 35).Should().BeFalse();
        builder.IsWhole.Should().BeFalse();
        builder.TryAddHeader(50, 3, 0, ScannedPlaceKind.Clean, floor: 45).Should().BeFalse();
        builder.TryAddTrailer(60, floor: 55).Should().BeFalse();
        var memo = builder.Complete(new ScannedTrailers());

        memo.Count.Should().Be(3);
        memo.IsWhole.Should().BeFalse();
        memo.FullAt.Should().Be(40);
        memo.WholeUntil.Should().Be(35);
        memo.Trailers.Should().BeNull();
        Headers(memo, 0, long.MaxValue).Should().Equal(Header(10, 1));
        memo.TryGetXRefKeywordAt(30, out _).Should().BeTrue();

        var past = new ScanMemo.Builder(1);
        past.TryAddHeader(10, 1, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        past.TryAddHeader(40, 2, 0, ScannedPlaceKind.Clean, floor: 60).Should().BeFalse();
        past.Complete(new ScannedTrailers()).WholeUntil.Should().Be(40);
    }

    [Theory]
    [InlineData(30L, 25L, false, 25L)]
    [InlineData(30L, 35L, false, 30L)]
    [InlineData(30L, 25L, true, 25L)]
    public void A_short_read_leaves_the_memo_whole_only_before_the_end_of_the_data_or_where_a_header_it_cuts_may_start(
        long end, long floor, bool fillsAfter, long wholeUntil)
    {
        // A window shorter than asked ends the data the scan read; a header whose obj lies past there may start as far back as
        // where the bytes the scan's last object left to read anew start. A memo that fills afterward, the scan going on past
        // a header it could not read back, is whole no further.
        var builder = new ScanMemo.Builder(1);
        builder.TryAddHeader(10, 1, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        builder.EndAt(end, floor);

        if (fillsAfter)
        {
            builder.TryAddHeader(40, 2, 0, ScannedPlaceKind.Clean, floor: 35).Should().BeFalse();
        }

        var memo = builder.Complete(new ScannedTrailers());

        (memo.IsWhole, memo.WholeUntil).Should().Be((!fillsAfter, wholeUntil));
    }

    [Theory]
    [InlineData("a short read", 20L, 20L)]
    [InlineData("the bound", 20L, 20L)]
    [InlineData("a short read", long.MaxValue, 25L)]
    [InlineData("the bound", long.MaxValue, 35L)]
    public void A_memo_ended_while_a_stream_s_length_is_awaited_is_whole_only_before_the_stream_s_data(string end, long awaitedFrom, long wholeUntil)
    {
        // The scan may yet hold the places it took past the data's start of a stream whose length's object it awaits as taken
        // between objects: a memo that a short read or its bound ends meanwhile is whole only before that start.
        var builder = new ScanMemo.Builder(1);
        builder.TryAddHeader(10, 1, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        builder.AwaitedFrom = awaitedFrom;

        if (end == "a short read")
        {
            builder.EndAt(30, floor: 25);
        }
        else
        {
            builder.TryAddHeader(40, 2, 0, ScannedPlaceKind.Clean, floor: 35).Should().BeFalse();
        }

        builder.Complete(new ScannedTrailers()).WholeUntil.Should().Be(wholeUntil);
    }

    private static ScannedHeader Header(long at, int number) => new(at, number, 0, ScannedPlaceKind.Clean);

    /// <summary>Gets the headers <paramref name="memo"/> holds from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static List<ScannedHeader> Headers(ScanMemo memo, long from, long to)
    {
        var headers = new List<ScannedHeader>();

        foreach (var header in memo.HeadersWithin(from, to))
        {
            headers.Add(header);
        }

        return headers;
    }

    /// <summary>Says what <paramref name="memo"/> holds: its headers, its keywords at each byte of the file, its trailers.</summary>
    private static List<string> Describe(ScanMemo memo, int length)
    {
        var described = Headers(memo, 0, long.MaxValue).Select(header => header.ToString()).ToList();

        for (var at = 0; at < length; at++)
        {
            if (memo.TryGetXRefKeywordAt(at, out var kind))
            {
                described.Add(string.Create(CultureInfo.InvariantCulture, $"xref at {at}, {kind}"));
            }
        }

        described.Add(string.Create(CultureInfo.InvariantCulture, $"{memo.Count} places, whole: {memo.IsWhole}, in order: {memo.InScanOrder}"));
        described.Add(string.Join(", ", memo.Trailers!.Keywords) + " / " + string.Join(", ", memo.Trailers.Streams));
        return described;
    }

    /// <summary>Gets the entries of the index <paramref name="document"/> holds, by number.</summary>
    private static List<string> Entries(PdfDocument document)
    {
        var entries = new List<string>();

        foreach (var (number, entry) in document.Reader.Index)
        {
            entries.Add(string.Create(CultureInfo.InvariantCulture, $"{number}: {entry}"));
        }

        entries.Sort(StringComparer.Ordinal);
        return entries;
    }

    /// <summary>An intact stream, object 7, whose stream keyword tells the data before it holds no intact stream's.</summary>
    private const string InnerStream = "7 0 obj\n<< /Length 3 >>\nstream\nabc\nendstream\nendobj\n";

    /// <summary>Ten objects, values, of numbers no row reads as 9.</summary>
    private static readonly string TenValues = string.Concat(
        new[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 20 }.Select(number => string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n({number})\nendobj\n")));

    /// <summary>
    /// Gives <paramref name="text"/> with <c>LENGTH</c> written as the length of the bytes <c>[[</c> and <c>]]</c> mark, the
    /// marks left out.
    /// </summary>
    private static string Measured(string text)
    {
        var start = text.IndexOf("[[", StringComparison.Ordinal);
        var end = text.IndexOf("]]", StringComparison.Ordinal);
        var length = (end - start - "[[".Length).ToString(CultureInfo.InvariantCulture);
        return text.Replace("[[", string.Empty, StringComparison.Ordinal).Replace("]]", string.Empty, StringComparison.Ordinal)
            .Replace("LENGTH", length, StringComparison.Ordinal);
    }

    /// <summary>Writes comment lines of 80 bytes into <paramref name="file"/>, the last shorter, up to <paramref name="end"/>.</summary>
    private static void PadWithComments(HandWrittenFile file, long end)
    {
        var line = "% " + new string('a', 77) + "\n";

        while (file.Position + line.Length <= end)
        {
            file.Raw(line);
        }

        if (file.Position < end)
        {
            file.Raw(new string('%', (int)(end - file.Position - 1)) + "\n");
        }
    }

    /// <summary>
    /// Writes a file of <paramref name="headers"/> object headers, each a place the memo holds in a slot of its own: objects 1
    /// and 2, then objects 3 and 4 defined again and again in turn, then the file's cross-reference stream 5, which names the
    /// last definitions in six rows however many headers the file writes, so that the index stays far below the memo's bound.
    /// </summary>
    private static byte[] Alternating(int headers)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
        };

        for (var header = 3; header < headers; header++)
        {
            var number = 3 + (header % 2);
            rows[number] = (1, file.Object(number, "null"), 0);
        }

        file.CrossReferenceStream(5, 6, rows, "/Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>Gives comment lines of <paramref name="length"/> bytes in all, at least two, each of 1,024 bytes but the last.</summary>
    private static string CommentLines(int length)
    {
        var text = new StringBuilder(length);

        while (text.Length < length)
        {
            var line = Math.Min(1024, length - text.Length);
            text.Append('%').Append('-', line - 2).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// Writes a sound file of objects 1 and 2, then <paramref name="text"/>, then its table, and gives where the byte
    /// <paramref name="place"/> bytes into the text lies.
    /// </summary>
    private static (byte[] File, long At) Sound(string text, int place)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        var at = file.Position + place;
        file.Raw(text);
        file.Table(3, rows, "/Size 3 /Root 1 0 R");
        return (file.Bytes, at);
    }

    /// <summary>
    /// Writes a file whose object 3 an update defines again, with a stream whose length is an object that holds a header and
    /// a keyword, and whose row for object 4 lies outside the file: reading 4 rebuilds the index.
    /// </summary>
    private static byte[] Updated(out long updated)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages), [3] = file.Object(3, "(three)") };
        file.Raw("% a comment that holds 8 0 obj\n");
        file.Raw("5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj xref\nendstream\nendobj\n");
        rows[4] = 999_999;
        var table = file.Table(5, rows, "/Size 5 /Root 1 0 R");
        updated = file.Object(3, "(three, updated)");
        var update = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \n3 2\n{updated:D10} 00000 n \n0000999999 00000 n \ntrailer\n<< /Size 5 /Root 1 0 R /Prev {table} >>\nstartxref\n{update}\n%%EOF\n"));
        return file.Bytes;
    }

    /// <summary>
    /// Writes a file whose object 3 is defined twenty times before its newest definition, each the same length where
    /// <paramref name="alike"/>, and whose row for object 4 lies outside the file: reading 4 rebuilds the index.
    /// </summary>
    private static byte[] Repeated(bool alike, out List<long> definitions)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        definitions = [];

        for (var i = 0; i < 20; i++)
        {
            definitions.Add(file.Object(3, alike ? "(old)" : string.Create(CultureInfo.InvariantCulture, $"(old {new string('x', i)})")));
        }

        definitions.Add(file.Object(3, "(newest)"));
        rows[3] = definitions[0];
        rows[4] = 999_999;
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// Opens a file whose object 7 is defined <paramref name="repeats"/> times, each a stream whose direct length an endstream
    /// confirms, its header <paramref name="distance"/> bytes past the one before, its data zeros the source does not hold.
    /// </summary>
    private static PdfDocument RepeatedStreams(int distance, int repeats)
    {
        const string End = "\nendstream\nendobj\n";
        var catalog = $"1 0 obj\n{Catalog}\nendobj\n";
        var head = Encoding.Latin1.GetBytes("%PDF-1.7\n" + catalog + $"2 0 obj\n{Pages}\nendobj\n");
        var dataLength = distance - Opening(0).Length - End.Length;
        var opening = Encoding.Latin1.GetBytes(Opening(dataLength));
        var end = Encoding.Latin1.GetBytes(End);
        List<(long At, byte[] Bytes)> pieces = [(0, head)];

        for (var i = 0; i < repeats; i++)
        {
            var at = head.Length + ((long)i * distance);
            pieces.Add((at, opening));
            pieces.Add((at + opening.Length + dataLength, end));
        }

        var table = head.Length + ((long)repeats * distance);
        var tail = Encoding.Latin1.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 3\n0000000000 65535 f \n0000000009 00000 n \n{9 + catalog.Length:D10} 00000 n \ntrailer\n<< /Size 3 /Root 1 0 R >>\nstartxref\n{table}\n%%EOF\n"));
        pieces.Add((table, tail));
        return PdfDocument.Open(new SparseSource(table + tail.Length, pieces), options: null, ownsSource: true);

        static string Opening(long length) => string.Create(CultureInfo.InvariantCulture, $"7 0 obj\n<< /Length {length:D10} >>\nstream\n");
    }

    /// <summary>
    /// A file that says it holds <paramref name="length"/> bytes and holds only <paramref name="pieces"/>, each where it starts:
    /// every other byte reads as zero.
    /// </summary>
    private sealed class SparseSource(long length, List<(long At, byte[] Bytes)> pieces) : PdfFileSource
    {
        /// <inheritdoc/>
        public override long Length => length;

        /// <inheritdoc/>
        public override int Read(long offset, Span<byte> buffer)
        {
            if (offset < 0 || offset >= length)
            {
                return 0;
            }

            var read = buffer[..(int)Math.Min(buffer.Length, length - offset)];
            read.Clear();

            foreach (var (at, bytes) in pieces)
            {
                var from = Math.Max(at, offset);
                var to = Math.Min(at + bytes.Length, offset + read.Length);

                if (from < to)
                {
                    bytes.AsSpan((int)(from - at), (int)(to - from)).CopyTo(read[(int)(from - offset)..]);
                }
            }

            return read.Length;
        }
    }

    /// <summary>A file read from memory that says it holds more bytes than it does: a read past them gives none.</summary>
    private sealed class OverlongSource(byte[] data, long extra) : PdfFileSource
    {
        /// <inheritdoc/>
        public override long Length => data.Length + extra;

        /// <inheritdoc/>
        public override int Read(long offset, Span<byte> buffer)
        {
            if (offset < 0 || offset >= data.Length)
            {
                return 0;
            }

            var count = (int)Math.Min(buffer.Length, data.Length - offset);
            data.AsSpan((int)offset, count).CopyTo(buffer);
            return count;
        }
    }

    /// <summary>A file read from memory that notes where each read starts.</summary>
    private sealed class RecordingSource(byte[] data, List<long> reads) : PdfFileSource
    {
        private readonly PdfFileSource _inner = FromMemory(data);

        /// <inheritdoc/>
        public override long Length => _inner.Length;

        /// <inheritdoc/>
        public override int Read(long offset, Span<byte> buffer)
        {
            reads.Add(offset);
            return _inner.Read(offset, buffer);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>A file read from memory whose read starting at one offset fails once, with an I/O error.</summary>
    private sealed class FailingSource(byte[] data) : PdfFileSource
    {
        private readonly PdfFileSource _inner = FromMemory(data);

        /// <summary>Gets or sets where the read that fails starts; -1 for none.</summary>
        public long FailAt { get; set; } = -1;

        /// <inheritdoc/>
        public override long Length => _inner.Length;

        /// <inheritdoc/>
        public override int Read(long offset, Span<byte> buffer)
        {
            if (offset == FailAt)
            {
                FailAt = -1;
                throw new IOException("The read failed.");
            }

            return _inner.Read(offset, buffer);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
