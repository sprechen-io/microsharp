namespace MicroSharp.Processing.Tests;

public sealed class Pcm16LeAlignmentBufferTests
{
    [Fact]
    public void Aligned_input_passes_through_by_reference()
    {
        var aligner = new Pcm16LeAlignmentBuffer();
        byte[] input = [1, 2, 3, 4];

        Assert.Same(input, aligner.Align(input));
        Assert.False(aligner.Complete());
    }

    [Fact]
    public void Copied_output_and_carried_byte_are_independent_of_input_arrays()
    {
        var aligner = new Pcm16LeAlignmentBuffer();
        byte[] first = [1, 2, 3];
        var firstOutput = aligner.Align(first);
        first.AsSpan().Clear();

        Assert.Empty(aligner.Align([]));
        byte[] second = [4, 5];
        var secondOutput = aligner.Align(second);
        second.AsSpan().Clear();

        Assert.Equal([1, 2], firstOutput);
        Assert.Equal([3, 4], secondOutput);
        Assert.Equal([5, 6], aligner.Align([6]));
        Assert.False(aligner.Complete());
    }

    [Fact]
    public void All_two_block_splits_emit_complete_samples_in_order()
    {
        byte[] input = [1, 2, 3, 4, 5, 6, 7];
        for (var split = 0; split <= input.Length; split++)
        {
            var aligner = new Pcm16LeAlignmentBuffer();
            var first = aligner.Align(input[..split]);
            var second = aligner.Align(input[split..]);

            Assert.Equal(split - split % 2, first.Length);
            Assert.Equal(input[..^1], first.Concat(second));
            Assert.Equal(0, second.Length % 2);
            Assert.True(aligner.Complete());
        }
    }

    [Fact]
    public void Completion_discards_partial_sample_without_padding_and_resets()
    {
        var aligner = new Pcm16LeAlignmentBuffer();
        Assert.Empty(aligner.Align([]));
        Assert.False(aligner.Complete());
        Assert.Empty(aligner.Align([0xFF]));
        Assert.True(aligner.Complete());
        Assert.False(aligner.Complete());
        Assert.Equal([1, 2], aligner.Align([1, 2]));
    }

    [Fact]
    public void Null_input_is_rejected_without_losing_the_carry()
    {
        var aligner = new Pcm16LeAlignmentBuffer();
        Assert.Empty(aligner.Align([1]));
        Assert.Throws<ArgumentNullException>(() => aligner.Align(null!));
        Assert.Equal([1, 2], aligner.Align([2]));
    }
}
