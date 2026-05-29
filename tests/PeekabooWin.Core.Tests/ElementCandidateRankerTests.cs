using Xunit;
using PeekabooWin.Core.Perception;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Tests;

public class ElementCandidateRankerTests
{
    private readonly ElementCandidateRanker _ranker = new();

    [Fact]
    public void Rank_ExactTextMatch_SemanticScoreIsOne()
    {
        var request = new CandidateRankRequest
        {
            TargetText = "Save",
            UiaCandidates = new List<UiElement>
            {
                new() { Label = "Save", BBox = new BoundingBox { X = 100, Y = 100, Width = 80, Height = 30 }, Confidence = 0.9 }
            }
        };

        var result = _ranker.Rank(request);

        Assert.NotNull(result.BestCandidate);
        Assert.Equal(1.0, result.BestCandidate!.SemanticScore);
    }

    [Fact]
    public void Rank_PartialTextMatch_SemanticScoreIsEightTenths()
    {
        var request = new CandidateRankRequest
        {
            TargetText = "Save",
            UiaCandidates = new List<UiElement>
            {
                new() { Label = "Save File", BBox = new BoundingBox { X = 100, Y = 100, Width = 80, Height = 30 }, Confidence = 0.8 }
            }
        };

        var result = _ranker.Rank(request);

        Assert.NotNull(result.BestCandidate);
        Assert.Equal(0.8, result.BestCandidate!.SemanticScore);
    }

    [Fact]
    public void Rank_NoMatchingCandidates_HasViableCandidateIsFalse()
    {
        var request = new CandidateRankRequest
        {
            TargetText = "Save",
            UiaCandidates = new List<UiElement>
            {
                new() { Label = "Completely Different", BBox = new BoundingBox { X = 100, Y = 100, Width = 80, Height = 30 }, Confidence = 0.0 }
            }
        };

        var result = _ranker.Rank(request);

        Assert.False(result.HasViableCandidate);
    }

    [Fact]
    public void Rank_MultipleUiaCandidates_SortedByFinalGroundingScoreDescending()
    {
        var request = new CandidateRankRequest
        {
            TargetText = "Submit",
            UiaCandidates = new List<UiElement>
            {
                new() { Label = "Cancel", BBox = new BoundingBox { X = 10, Y = 10, Width = 80, Height = 30 }, Confidence = 0.5 },
                new() { Label = "Submit", BBox = new BoundingBox { X = 200, Y = 200, Width = 80, Height = 30 }, Confidence = 0.95 }
            }
        };

        var result = _ranker.Rank(request);

        Assert.Equal(2, result.TotalCandidates);
        Assert.True(result.RankedCandidates[0].FinalGroundingScore >= result.RankedCandidates[1].FinalGroundingScore);
    }

    [Fact]
    public void Rank_OcrCandidate_SourceIsOcr()
    {
        var request = new CandidateRankRequest
        {
            TargetText = "Hello",
            OcrCandidates = new List<OcrWord>
            {
                new() { Text = "Hello", BoundingBox = new OcrRect { X = 50, Y = 50, Width = 100, Height = 20 }, Confidence = 0.9 }
            }
        };

        var result = _ranker.Rank(request);

        Assert.NotNull(result.BestCandidate);
        Assert.Equal("ocr", result.BestCandidate!.Source);
    }

    [Fact]
    public void Rank_PreferredRegionBottomWithCandidateInBottom_LayoutScoreBoosted()
    {
        var viewport = new BoundingBox { X = 0, Y = 0, Width = 1000, Height = 1000 };

        var request = new CandidateRankRequest
        {
            TargetText = "OK",
            PreferredRegion = "bottom",
            Viewport = viewport,
            UiaCandidates = new List<UiElement>
            {
                new() { Label = "OK", BBox = new BoundingBox { X = 400, Y = 800, Width = 80, Height = 30 }, Confidence = 0.8 }
            }
        };

        var result = _ranker.Rank(request);

        Assert.NotNull(result.BestCandidate);
        Assert.Equal(1.0, result.BestCandidate!.LayoutScore);
    }

    [Fact]
    public void Rank_DeduplicatesOverlappingCandidatesWithSameText()
    {
        var request = new CandidateRankRequest
        {
            TargetText = "Save",
            UiaCandidates = new List<UiElement>
            {
                new() { Label = "Save", BBox = new BoundingBox { X = 100, Y = 100, Width = 80, Height = 30 }, Confidence = 0.7 },
                new() { Label = "Save", BBox = new BoundingBox { X = 102, Y = 101, Width = 80, Height = 30 }, Confidence = 0.9 }
            },
            OcrCandidates = new List<OcrWord>
            {
                new() { Text = "Save", BoundingBox = new OcrRect { X = 101, Y = 100, Width = 80, Height = 30 }, Confidence = 0.6 }
            }
        };

        var result = _ranker.Rank(request);

        var saveCandidates = result.RankedCandidates.Where(c => string.Equals(c.Text, "Save", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(saveCandidates);
    }

    [Fact]
    public void Rank_BestCandidateIsHighestScoring()
    {
        var request = new CandidateRankRequest
        {
            TargetText = "Open",
            UiaCandidates = new List<UiElement>
            {
                new() { Label = "Open", BBox = new BoundingBox { X = 100, Y = 100, Width = 80, Height = 30 }, Confidence = 0.99 },
                new() { Label = "Open Recent", BBox = new BoundingBox { X = 200, Y = 200, Width = 80, Height = 30 }, Confidence = 0.5 }
            }
        };

        var result = _ranker.Rank(request);

        Assert.NotNull(result.BestCandidate);
        Assert.Equal("Open", result.BestCandidate!.Text);
        foreach (var candidate in result.RankedCandidates.Skip(1))
        {
            Assert.True(result.BestCandidate.FinalGroundingScore >= candidate.FinalGroundingScore);
        }
    }
}
