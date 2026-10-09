using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Broker;

internal sealed record BrokerReviewCard(string Title, IReadOnlyList<string> Lines, ChangeCategory? Category);
