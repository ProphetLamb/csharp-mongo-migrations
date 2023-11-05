using System.Collections.Immutable;
using Xunit;

namespace MongoDB.Migration.Test;

public class MigrationCompletionServiceTests
{
    [Fact]
    public void Given__CompletedMigrations_WaitReuturnsTheCompletion()
    {
        MigrationCompletionService completion = new();
        completion.WithKnownDatabaseAliases(ImmutableHashSet.Create("one", "two", "three"));

        completion.MigrationCompleted(new("onedb", "one", 0));
        completion.MigrationCompleted(new("twodb", "two", 1));

        Assert.True(completion.WaitAsync("one").IsCompletedSuccessfully);
        Assert.Equal(0, completion.WaitAsync("one").GetAwaiter().GetResult()?.Version);

        Assert.True(completion.WaitAsync("two").IsCompletedSuccessfully);
        Assert.Equal(1, completion.WaitAsync("two").GetAwaiter().GetResult()?.Version);

        Assert.False(completion.WaitAsync("three").IsCompleted);

        completion.MigrationCompleted(new("threedb", "three", 2));

        Assert.True(completion.WaitAsync("three").IsCompletedSuccessfully);
        Assert.Equal(2, completion.WaitAsync("three").GetAwaiter().GetResult()?.Version);
    }
}
