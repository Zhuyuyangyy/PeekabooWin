using System.Collections.Generic;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Ocr;

public interface IOcrEngine
{
    string Name { get; }
    string DefaultLanguage { get; }
    OcrResult Recognize(Bitmap bitmap);
    OcrResult Recognize(string imagePath);
    List<OcrWord> FindWords(OcrResult result, string keyword, bool caseSensitive = false);
    (int x, int y)? FindWordCenter(OcrResult result, string keyword, bool caseSensitive = false);
}
