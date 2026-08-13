using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Web.Responses;
using JetBrains.Annotations;
using SteamKit2;

namespace RandomProfileBackground;

#pragma warning disable CA1812 // ASF uses this class during runtime
#pragma warning disable CA1001 // Plugin instances live for the process' lifetime; ASF gives IPlugin implementations no disposal hook to call into
#pragma warning disable CA5394 // Randomness here only picks an arbitrary item/delay, it's not used for anything security-sensitive
[UsedImplicitly]
internal sealed class RandomProfileBackground : IASF, IBotConnection, IGitHubPluginUpdates {
	private const ushort DefaultMaxDelayInDays = 60;
	private const ushort DefaultMinDelayInDays = 14;

	private static readonly Uri SteamApiURL = new("https://api.steampowered.com");
	private static readonly Uri SteamCommunityURL = new("https://steamcommunity.com");

	private readonly ConcurrentDictionary<string, CancellationTokenSource> BotLoops = new(StringComparer.Ordinal);

	// Last equipped item per bot, purely so we don't roll the same one twice in a row
	private readonly ConcurrentDictionary<string, string> BotLastItemID = new(StringComparer.Ordinal);

	private bool Enabled;
	private volatile bool EmptyInventoryWarningLogged;
	private ushort MaxDelayInDays = DefaultMaxDelayInDays;
	private ushort MinDelayInDays = DefaultMinDelayInDays;

	public string Name => nameof(RandomProfileBackground);
	public string RepositoryName => "buddymurdock/ASF-RandomProfileBackground";
	public Version Version => typeof(RandomProfileBackground).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	// Reads RandomProfileBackgroundEnabled / RandomProfileBackgroundMinDelayDays / RandomProfileBackgroundMaxDelayDays from the global ASF.json config
	public Task OnASFInit(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null) {
		if (additionalConfigProperties != null) {
			foreach ((string configProperty, JsonElement configValue) in additionalConfigProperties) {
				switch (configProperty) {
					case $"{nameof(RandomProfileBackground)}Enabled" when configValue.ValueKind is JsonValueKind.True or JsonValueKind.False:
						Enabled = configValue.GetBoolean();

						break;
					case $"{nameof(RandomProfileBackground)}MinDelayDays" when (configValue.ValueKind == JsonValueKind.Number) && configValue.TryGetUInt16(out ushort minDelay) && (minDelay > 0):
						MinDelayInDays = minDelay;

						break;
					case $"{nameof(RandomProfileBackground)}MaxDelayDays" when (configValue.ValueKind == JsonValueKind.Number) && configValue.TryGetUInt16(out ushort maxDelay) && (maxDelay > 0):
						MaxDelayInDays = maxDelay;

						break;
				}
			}
		}

		if (MinDelayInDays > MaxDelayInDays) {
			(MinDelayInDays, MaxDelayInDays) = (MaxDelayInDays, MinDelayInDays);
		}

		if (!Enabled) {
			ASF.ArchiLogger.LogGenericInfo($"{Name} is disabled, set {nameof(RandomProfileBackground)}Enabled to true in ASF.json to turn it on.");

			return Task.CompletedTask;
		}

		ASF.ArchiLogger.LogGenericInfo($"{Name} is enabled, every {MinDelayInDays}-{MaxDelayInDays} days each bot randomly equips one of its own already-owned profile/mini-profile backgrounds.");

		return Task.CompletedTask;
	}

	public Task OnLoaded() {
		ASF.ArchiLogger.LogGenericInfo($"{Name} has been loaded!");

		return Task.CompletedTask;
	}

	public async Task OnBotDisconnected(Bot bot, EResult reason) {
		if (BotLoops.TryRemove(bot.BotName, out CancellationTokenSource? cts)) {
			await cts.CancelAsync().ConfigureAwait(false);
			cts.Dispose();
		}
	}

	public Task OnBotLoggedOn(Bot bot) {
		if (!Enabled) {
			return Task.CompletedTask;
		}

		CancellationTokenSource cts = new();

		if (!BotLoops.TryAdd(bot.BotName, cts)) {
			// A loop for this bot is already running, nothing to do
			cts.Dispose();

			return Task.CompletedTask;
		}

		Utilities.InBackground(() => BotBackgroundLoopAsync(bot, cts.Token), true);

		return Task.CompletedTask;
	}

	private async Task BotBackgroundLoopAsync(Bot bot, CancellationToken cancellationToken) {
		while (!cancellationToken.IsCancellationRequested) {
			int delayDays = MinDelayInDays == MaxDelayInDays ? MinDelayInDays : Random.Shared.Next(MinDelayInDays, MaxDelayInDays + 1);

			try {
				await Task.Delay(TimeSpan.FromDays(delayDays), cancellationToken).ConfigureAwait(false);
			} catch (OperationCanceledException) {
				break;
			}

			if (cancellationToken.IsCancellationRequested || !bot.IsConnectedAndLoggedOn) {
				break;
			}

			try {
				await TrySetRandomBackgroundAsync(bot).ConfigureAwait(false);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}
		}
	}

	private async Task TrySetRandomBackgroundAsync(Bot bot) {
		string? token = bot.AccessToken;

		if (string.IsNullOrEmpty(token)) {
			bot.ArchiLogger.LogGenericWarning($"{nameof(RandomProfileBackground)} needs a valid access token, but none is available for this bot yet.");

			return;
		}

		Uri ownedRequest = new(SteamApiURL, $"/IPlayerService/GetProfileItemsOwned/v1/?access_token={token}&language=english");

		ObjectResponse<ProfileItemsOwnedEnvelope>? ownedResponse = await bot.ArchiWebHandler.UrlGetToJsonObjectWithSession<ProfileItemsOwnedEnvelope>(ownedRequest, referer: SteamCommunityURL).ConfigureAwait(false);
		ProfileItemsOwnedResponse? owned = ownedResponse?.Content?.Response;

		List<(string CommunityItemID, bool IsMini)> candidates = [
			.. (owned?.ProfileBackgrounds ?? []).Where(static item => !string.IsNullOrEmpty(item.CommunityItemID)).Select(static item => (item.CommunityItemID!, false)),
			.. (owned?.MiniProfileBackgrounds ?? []).Where(static item => !string.IsNullOrEmpty(item.CommunityItemID)).Select(static item => (item.CommunityItemID!, true))
		];

		if (candidates.Count == 0) {
			if (!EmptyInventoryWarningLogged) {
				EmptyInventoryWarningLogged = true;

				ASF.ArchiLogger.LogGenericWarning($"{nameof(RandomProfileBackground)} couldn't find any owned profile/mini-profile backgrounds on any bot - these come from crafting Steam trading card badges, nothing to equip until at least one bot has some.");
			}

			return;
		}

		BotLastItemID.TryGetValue(bot.BotName, out string? lastItemID);

		List<(string CommunityItemID, bool IsMini)> pool = (candidates.Count > 1) && (lastItemID != null) ? [.. candidates.Where(item => item.CommunityItemID != lastItemID)] : candidates;

		(string communityItemID, bool isMini) = pool[Random.Shared.Next(pool.Count)];

		bool success = await EquipBackgroundAsync(bot, token, communityItemID, isMini).ConfigureAwait(false);

		if (success) {
			BotLastItemID[bot.BotName] = communityItemID;

			bot.ArchiLogger.LogGenericInfo($"Randomly equipped {(isMini ? "mini-profile" : "profile")} background {communityItemID}.");
		} else {
			bot.ArchiLogger.LogGenericWarning($"Failed to equip {(isMini ? "mini-profile" : "profile")} background {communityItemID}.");
		}
	}

	// SetProfileBackground/SetMiniProfileBackground authenticate purely via access_token in the query string
	// (same modern IPlayerService family as the rest of Steam's post-2020 profile customization), so this
	// deliberately skips ASF's usual sessionid injection (ESession.None) - it's neither needed nor sent.
	private static async Task<bool> EquipBackgroundAsync(Bot bot, string token, string communityItemID, bool isMini) {
		string method = isMini ? "SetMiniProfileBackground" : "SetProfileBackground";
		Uri request = new(SteamApiURL, $"/IPlayerService/{method}/v1/?access_token={token}");

		Dictionary<string, string> data = new(StringComparer.Ordinal) {
			{ "input_json", JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal) { { "communityitemid", communityItemID } }) }
		};

		return await bot.ArchiWebHandler.UrlPostWithSession(request, data: data, referer: SteamCommunityURL, session: ESession.None).ConfigureAwait(false);
	}

	private sealed record ProfileItemsOwnedEnvelope([property: JsonPropertyName("response")] ProfileItemsOwnedResponse? Response);

	private sealed record ProfileItemsOwnedResponse(
		[property: JsonPropertyName("profile_backgrounds")] List<ProfileItemData>? ProfileBackgrounds,
		[property: JsonPropertyName("mini_profile_backgrounds")] List<ProfileItemData>? MiniProfileBackgrounds
	);

	private sealed record ProfileItemData([property: JsonPropertyName("communityitemid")] string? CommunityItemID);
}
#pragma warning restore CA5394
#pragma warning restore CA1001
#pragma warning restore CA1812
