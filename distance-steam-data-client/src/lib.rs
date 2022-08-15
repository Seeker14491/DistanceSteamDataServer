pub use steam::{LeaderboardEntry, LeaderboardResponse};

use anyhow::Result;
use steam::{steam_client::SteamClient, LeaderboardRequest, PersonaRequest};

#[derive(Debug, Clone)]
pub struct Client(SteamClient<tonic::transport::Channel>);

impl Client {
    pub async fn connect(address: impl Into<String>) -> Result<Self> {
        let inner = SteamClient::connect(address.into()).await?;

        Ok(Client(inner))
    }

    /// Gets the total number of entries on the given leaderboard.
    ///
    /// This method should generally only be used if you don't need the leaderboard entries, as
    /// fetching the entries provides this data without needing a separate call.
    pub async fn leaderboard_entry_count(&self, leaderboard_name: &str) -> Result<i32> {
        let response = self
            .leaderboard_entries_range(leaderboard_name, 0, 0)
            .await?;

        Ok(response.total_entries)
    }

    /// Fetches a range of entries for a given leaderboard. Also returns the total number of
    /// entries on the leaderboard.
    ///
    /// For example, to fetch the first 10 entries, use `start_index = 1` and `end_index = 10`. It
    /// is ok to request more entries than exist.
    pub async fn leaderboard_entries_range(
        &self,
        leaderboard_name: &str,
        start_index: i32,
        end_index: i32,
    ) -> Result<LeaderboardResponse> {
        let response = self
            .0
            .clone()
            .get_leaderboard_entries(LeaderboardRequest {
                leaderboard_name: leaderboard_name.into(),
                start_index,
                end_index,
            })
            .await?
            .into_inner();

        Ok(response)
    }

    /// Fetches all entries for a given leaderboard.
    pub async fn leaderboard_entries_all(
        &self,
        leaderboard_name: &str,
    ) -> Result<Vec<LeaderboardEntry>> {
        let entries = self
            .leaderboard_entries_range(leaderboard_name, 1, i32::MAX)
            .await?
            .entries;

        Ok(entries)
    }

    pub async fn persona_names(
        &self,
        steam_ids: Vec<u64>,
    ) -> Result<impl Iterator<Item = Option<String>>> {
        let response = self
            .0
            .clone()
            .get_persona_names(PersonaRequest { steam_ids })
            .await?
            .into_inner();

        let empty_strings_mapped_to_none =
            response
                .persona_names
                .into_iter()
                .map(|s| if s.is_empty() { None } else { Some(s) });

        Ok(empty_strings_mapped_to_none)
    }
}

#[allow(clippy::all)]
mod steam {
    tonic::include_proto!("steam");
}
