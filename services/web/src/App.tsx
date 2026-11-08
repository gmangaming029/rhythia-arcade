import { useEffect, useMemo, useState } from "react";
import {
  Button,
  Card,
  Input,
  Select,
  Spinner,
  Tab,
  TabList,
  Text,
} from "@fluentui/react-components";
import {
  ArrowDownloadRegular,
  MusicNote2Regular,
  SearchRegular,
  ShieldTaskRegular,
  TrophyRegular,
} from "@fluentui/react-icons";
import {
  decideRankedReview,
  downloadChartPackage,
  getChart,
  getLeaderboard,
  getModerationReports,
  getPlayerRp,
  getRankedReviews,
  nominateChart,
  reportChart,
  searchCharts,
  takedownChart,
  uploadChartPackage,
  type ModerationReport,
  type PlayerRp,
  type RankedNomination,
  type RankedPlayer,
} from "./api";
import { authConfigured, getDisplayName, userManager } from "./auth/oidc";
import type { Chart } from "./types/repository";

type View = "catalog" | "ranked" | "uploads" | "player" | "moderation";

export default function App() {
  const [view, setView] = useState<View>("catalog");
  const [query, setQuery] = useState("");
  const [difficulty, setDifficulty] = useState("");
  const [tag, setTag] = useState("");
  const [sort, setSort] = useState<"updated" | "popular">("updated");
  const [charts, setCharts] = useState<Chart[]>([]);
  const [selected, setSelected] = useState<Chart>();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [signedIn, setSignedIn] = useState(false);
  const [userName, setUserName] = useState("");
  const [playerId, setPlayerId] = useState("");
  const [leaderboard, setLeaderboard] = useState<RankedPlayer[]>([]);
  const [reviewQueue, setReviewQueue] = useState<RankedNomination[]>([]);
  const [reports, setReports] = useState<ModerationReport[]>([]);
  const [rp, setRp] = useState<PlayerRp>();
  const [queueError, setQueueError] = useState("");
  const [moderationError, setModerationError] = useState("");
  const [selectedPackage, setSelectedPackage] = useState<File>();
  const [uploading, setUploading] = useState(false);
  const [uploadFeedback, setUploadFeedback] = useState("");
  const [actionFeedback, setActionFeedback] = useState("");
  const [nominationRationale, setNominationRationale] = useState("");
  const [reportReason, setReportReason] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!userManager) return;
    const updateUser = (user: Awaited<ReturnType<typeof userManager.getUser>>) => {
      const activeUser = Boolean(user && !user.expired);
      setSignedIn(activeUser);
      setUserName(getDisplayName(user && !user.expired ? user : null));
      const subject = user?.profile.sub;
      if (typeof subject === "string") setPlayerId(subject);
    };
    const callback = new URL(window.location.href);
    const initial = callback.searchParams.has("code")
      ? userManager.signinRedirectCallback().then((user) => {
          window.history.replaceState({}, document.title, callback.pathname);
          updateUser(user);
        })
      : userManager.getUser().then(updateUser);
    void initial.catch((reason: unknown) => {
      setError(reason instanceof Error ? reason.message : "Could not restore sign-in state.");
    });
    const refresh = () => void userManager.getUser().then(updateUser);
    userManager.events.addUserLoaded(refresh);
    userManager.events.addUserUnloaded(refresh);
    return () => {
      userManager.events.removeUserLoaded(refresh);
      userManager.events.removeUserUnloaded(refresh);
    };
  }, []);

  useEffect(() => {
    let active = true;
    if (!signedIn) {
      setCharts([]);
      setLoading(false);
      setError(authConfigured
        ? "Sign in to browse the authenticated chart repository."
        : "Sign-in is not configured. Set the OIDC values in services/web/.env.local to access the API.");
      return () => { active = false; };
    }
    setLoading(true);
    setError("");
    void searchCharts({
      q: query,
      difficulty: difficulty || undefined,
      tag: tag.trim() ? [tag.trim()] : undefined,
      sort,
    })
      .then((result) => { if (active) setCharts(result.items); })
      .catch((reason: unknown) => { if (active) setError(reason instanceof Error ? reason.message : "Could not load charts."); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [query, difficulty, tag, sort, signedIn]);

  useEffect(() => {
    if (!signedIn || view !== "ranked") return;
    setQueueError("");
    void getLeaderboard()
      .then((result) => setLeaderboard(result.items))
      .catch((reason: unknown) => setQueueError(reason instanceof Error ? reason.message : "Could not load the RP leaderboard."));
    void getRankedReviews()
      .then((result) => setReviewQueue(result.items))
      .catch((reason: unknown) => setQueueError((current) =>
        [current, reason instanceof Error ? reason.message : "Ranked review access is unavailable."].filter(Boolean).join(" ")));
  }, [signedIn, view]);

  useEffect(() => {
    if (!signedIn || view !== "moderation") return;
    setModerationError("");
    void getModerationReports()
      .then((result) => setReports(result.items))
      .catch((reason: unknown) => setModerationError(reason instanceof Error ? reason.message : "Could not load reports."));
  }, [signedIn, view]);

  const title = useMemo(() => ({
    catalog: "Chart library",
    ranked: "Ranked & RP",
    uploads: "Creator studio",
    player: "Player RP",
    moderation: "Moderation",
  }[view]), [view]);

  async function signIn() {
    if (userManager) await userManager.signinRedirect();
  }

  async function signOut() {
    if (userManager) await userManager.signoutRedirect();
  }

  async function showChart(chartId: string) {
    setActionFeedback("");
    try {
      setSelected(await getChart(chartId));
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Could not load chart details.");
    }
  }

  async function download(chart: Chart) {
    setBusy(true);
    setActionFeedback("");
    try {
      await downloadChartPackage(chart);
      setActionFeedback("Package checksum verified and download started.");
    } catch (reason) {
      setActionFeedback(reason instanceof Error ? reason.message : "Package download failed.");
    } finally {
      setBusy(false);
    }
  }

  async function submitUpload() {
    if (!selectedPackage) return;
    setUploading(true);
    setUploadFeedback("");
    try {
      const result = await uploadChartPackage(selectedPackage);
      setUploadFeedback(`Published unranked chart ${result.chartId} (version ${result.versionId}); automated package checks passed.`);
      setSelectedPackage(undefined);
      setView("catalog");
      setQuery("");
    } catch (reason) {
      setUploadFeedback(reason instanceof Error ? reason.message : "Package upload failed.");
    } finally {
      setUploading(false);
    }
  }

  async function nominateSelected() {
    if (!selected || !nominationRationale.trim()) return;
    setBusy(true);
    try {
      const result = await nominateChart(selected.chartId, nominationRationale.trim());
      setActionFeedback(`Ranked nomination ${result.status}; it is separate from public unranked availability.`);
      setNominationRationale("");
    } catch (reason) {
      setActionFeedback(reason instanceof Error ? reason.message : "Could not submit ranked nomination.");
    } finally {
      setBusy(false);
    }
  }

  async function reportSelected() {
    if (!selected || !reportReason.trim()) return;
    setBusy(true);
    try {
      await reportChart(selected.chartId, reportReason.trim());
      setActionFeedback("Report submitted for maintainer review.");
      setReportReason("");
    } catch (reason) {
      setActionFeedback(reason instanceof Error ? reason.message : "Could not submit report.");
    } finally {
      setBusy(false);
    }
  }

  async function decide(nomination: RankedNomination, decision: "approved" | "rejected") {
    const rationale = window.prompt(`Rationale for ${decision} this ranked nomination:`);
    if (!rationale?.trim()) return;
    try {
      await decideRankedReview(nomination.nominationId, decision, rationale.trim());
      setReviewQueue((current) => current.filter((entry) => entry.nominationId !== nomination.nominationId));
      setQueueError("");
    } catch (reason) {
      setQueueError(reason instanceof Error ? reason.message : "Could not record review decision.");
    }
  }

  async function takedown(report: ModerationReport) {
    const rationale = window.prompt("Maintainer rationale for taking down this chart:");
    if (!rationale?.trim()) return;
    try {
      await takedownChart(report.chartId, rationale.trim());
      setReports((current) => current.filter((entry) => entry.reportId !== report.reportId));
      setModerationError("");
    } catch (reason) {
      setModerationError(reason instanceof Error ? reason.message : "Could not take down chart.");
    }
  }

  async function lookUpRp() {
    if (!playerId.trim()) return;
    setRp(undefined);
    try {
      setRp(await getPlayerRp(playerId.trim()));
    } catch (reason) {
      setActionFeedback(reason instanceof Error ? reason.message : "Could not load player RP.");
    }
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand"><span className="brand-mark">R</span><span>RHYTHIA<span className="brand-sub">CHART REPOSITORY</span></span></div>
        <div className="side-label">REPOSITORY</div>
        <TabList vertical selectedValue={view} onTabSelect={(_, data) => setView(data.value as View)}>
          <Tab value="catalog" icon={<MusicNote2Regular />}>Browse charts</Tab>
          <Tab value="ranked" icon={<TrophyRegular />}>Ranked & RP</Tab>
          <Tab value="uploads" icon={<ShieldTaskRegular />}>Creator studio</Tab>
          <Tab value="player">Player RP</Tab>
          <Tab value="moderation">Moderation</Tab>
        </TabList>
        <div className="sidebar-note">
          <div className="status-dot" />
          <div><strong>Local MVP repository</strong><span>Package files stay on the configured host</span></div>
        </div>
      </aside>

      <main className="main-area">
        <header className="topbar">
          <div className="breadcrumbs">Repository <span>/</span> {title}</div>
          <div className="account">
            {signedIn
              ? <><Text className="username">{userName || "Signed in player"}</Text><Button appearance="subtle" onClick={() => void signOut()}>Sign out</Button></>
              : <Button appearance="primary" onClick={() => void signIn()} disabled={!authConfigured}>Sign in</Button>}
          </div>
        </header>

        <section className="content">
          <div className="page-heading">
            <div><div className="eyebrow">COMMUNITY CATALOG</div><h1>{title}</h1>
              <p>{view === "catalog"
                ? "Search authenticated community charts and download their complete asset packages."
                : view === "ranked"
                  ? "Ranked eligibility is separately reviewed; public community charts remain unranked until approved."
                  : view === "uploads"
                    ? "Upload a ZIP package for automated checks and immediate unranked publication."
                    : view === "player"
                      ? "Look up a player's current server-calculated Ranked Points total."
                      : "Maintainers review reports and can remove public charts with an auditable rationale."}</p>
            </div>
            {view === "catalog" && <div className="chart-count"><strong>{charts.length.toString().padStart(2, "0")}</strong><span>CHARTS</span></div>}
          </div>

          {view === "catalog" && <>
            <div className="filter-bar">
              <div className="search-wrap"><SearchRegular /><Input aria-label="Search title, creator, or tags" value={query} onChange={(_, data) => setQuery(data.value)} placeholder="Search title, creator, tags…" /></div>
              <Select aria-label="Difficulty" value={difficulty} onChange={(_, data) => setDifficulty(data.value)}>
                <option value="">All difficulties</option><option>Beginner</option><option>Intermediate</option><option>Advanced</option><option>Expert</option>
              </Select>
              <Input aria-label="Filter by tag" value={tag} onChange={(_, data) => setTag(data.value)} placeholder="Tag" />
              <Select aria-label="Sort charts" value={sort} onChange={(_, data) => setSort(data.value as "updated" | "popular")}>
                <option value="updated">Recently updated</option><option value="popular">Most downloaded</option>
              </Select>
            </div>
            {loading ? <div className="loading"><Spinner label="Loading charts" /></div> : error ? <div className="notice error">{error}</div> :
              charts.length === 0 ? <div className="empty"><MusicNote2Regular /><h2>No charts found</h2><p>Try another search or clear the difficulty filter.</p></div> :
                <div className="chart-grid">{charts.map((chart, index) =>
                  <Card className="chart-card" key={chart.chartId} onClick={() => void showChart(chart.chartId)}>
                    <div className={`cover cover-${index % 3}`}><span className="cover-glyph">{["◈", "⌁", "✳"][index % 3]}</span><span className="cover-label">RHYTHIA COMMUNITY</span></div>
                    <div className="card-content"><div className="card-title-row"><div><h2>{chart.title}</h2><span className="creator">by {chart.creator}</span></div><span className="difficulty">{chart.difficulty}</span></div>
                      <p className="description">{chart.description}</p><div className="tags">{chart.tags.map((item) => <span key={item}>{item}</span>)}</div>
                      <div className="card-footer"><span>Updated {new Date(chart.updatedAt).toLocaleDateString()}</span><Button appearance="subtle" icon={<ArrowDownloadRegular />} aria-label={`Download ${chart.title}`} onClick={(event) => { event.stopPropagation(); void download(chart); }}>Download</Button></div>
                    </div>
                  </Card>)}</div>}
          </>}

          {view === "ranked" && <div className="feature-panel"><TrophyRegular /><div>
            <h2>Ranked review and Ranked Points</h2>
            <p>A public upload is unranked by default. Ranked access requires a separate reviewer decision recorded with rationale. Ranked result submission currently returns unavailable until an approved, versioned RP formula is configured; this page does not claim or award RP from unconfigured scoring.</p>
            <div className="pill">PUBLIC UNRANKED PUBLISHING ≠ RANKED APPROVAL</div>
            {queueError && <div className="notice">{queueError}</div>}
            <h3>RP leaderboard</h3>
            {leaderboard.length === 0 ? <p>No accepted ranked results are available.</p> :
              <ol>{leaderboard.map((entry) => <li key={entry.playerId}>#{entry.rank} — {entry.playerId}: {entry.rp} RP</li>)}</ol>}
            <h3>Pending ranked nominations</h3>
            {reviewQueue.length === 0 ? <p>No pending nominations, or reviewer access is not available for this account.</p> :
              reviewQueue.map((entry) => <Card className="feature-panel" key={entry.nominationId}>
                <div><strong>Chart {entry.chartId}</strong><p>{entry.rationale}</p><p className="muted">Nominated by {entry.playerId} · {new Date(entry.createdAt).toLocaleString()}</p>
                  <Button onClick={() => void decide(entry, "approved")}>Approve ranked eligibility</Button>{" "}
                  <Button onClick={() => void decide(entry, "rejected")}>Reject</Button>
                </div>
              </Card>)}
          </div></div>}

          {view === "uploads" && <Card className="feature-panel"><ShieldTaskRegular /><div>
            <h2>Upload a chart package</h2>
            <p>Choose a ZIP containing a root manifest.json, chart data, and its required audio, images, and other assets. The API applies automated structure and size checks. Valid packages publish immediately as public <strong>unranked</strong> charts; ranked eligibility always requires a separate vetted nomination and review.</p>
            <p className="muted">Upload and package access require sign-in. An identity provider must be configured by the operator.</p>
            <input aria-label="Chart package ZIP file" type="file" accept=".zip,application/zip" onChange={(event) => setSelectedPackage(event.target.files?.[0])} />
            <Button appearance="primary" disabled={!signedIn || !selectedPackage || uploading} onClick={() => void submitUpload()}>
              {uploading ? "Uploading and validating…" : "Upload package"}
            </Button>
            {uploadFeedback && <p role="status">{uploadFeedback}</p>}
          </div></Card>}

          {view === "player" && <Card className="feature-panel"><TrophyRegular /><div>
            <h2>Player RP lookup</h2>
            <p>RP totals are read from the repository API. Ranked results are not accepted while this deployment has no approved versioned scoring formula.</p>
            <Input aria-label="Player subject ID" value={playerId} onChange={(_, data) => setPlayerId(data.value)} placeholder="Player subject ID" />
            <Button onClick={() => void lookUpRp()} disabled={!signedIn || !playerId.trim()}>Load player RP</Button>
            {rp && <p><strong>{rp.playerId}</strong>: {rp.rp} RP</p>}
            {actionFeedback && <p role="status">{actionFeedback}</p>}
          </div></Card>}

          {view === "moderation" && <div className="feature-panel"><ShieldTaskRegular /><div>
            <h2>Open community reports</h2>
            <p>Report handling and takedowns are maintainer-only actions. Takedown requests are recorded with a rationale. Ordinary publication is automated, not a human pre-publication queue.</p>
            {moderationError && <div className="notice">{moderationError}</div>}
            {reports.length === 0 ? <p>No reports available, or maintainer access is not available for this account.</p> :
              reports.map((report) => <Card className="feature-panel" key={report.reportId}>
                <div><strong>Chart {report.chartId}</strong><p>{report.reason}</p><p className="muted">Reported by {report.reporterId} · {new Date(report.createdAt).toLocaleString()}</p>
                  <Button onClick={() => void takedown(report)}>Take down with rationale</Button>
                </div>
              </Card>)}
          </div></div>}

          <footer className="footer"><span>RHYTHIA · COMMUNITY CHARTS</span><span>Local MVP · worldwide access requires a public API and package host</span></footer>
        </section>
      </main>

      {selected && <div className="dialog-backdrop" role="presentation" onClick={() => setSelected(undefined)}>
        <section className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="detail-title" onClick={(event) => event.stopPropagation()}>
          <button className="close" onClick={() => setSelected(undefined)} aria-label="Close details">×</button>
          <div className="eyebrow">CHART DETAILS</div><h2 id="detail-title">{selected.title}</h2><div className="creator">by {selected.creator}</div>
          <p>{selected.description}</p><div className="detail-stats"><span>DIFFICULTY<strong>{selected.difficulty}</strong></span><span>VERSION<strong>{selected.versionId}</strong></span><span>PACKAGE<strong>{(selected.sizeBytes / 1_000_000).toFixed(1)} MB</strong></span></div>
          <div className="pill">{selected.rankedState === "ranked" ? "RANKED · APPROVED" : "UNRANKED · COMMUNITY PUBLISHED"}</div>
          <p className="muted">SHA-256: {selected.sha256}</p>
          {!signedIn && <p className="muted">Sign in is required to download packages and submit chart actions.</p>}
          <Button appearance="primary" icon={<ArrowDownloadRegular />} disabled={!signedIn || busy} onClick={() => void download(selected)}>Download & verify checksum</Button>
          <h3>Nominate for separate ranked review</h3>
          <Input aria-label="Ranked nomination rationale" value={nominationRationale} onChange={(_, data) => setNominationRationale(data.value)} placeholder="Why should this chart be considered for ranked play?" />
          <Button disabled={!signedIn || busy || !nominationRationale.trim()} onClick={() => void nominateSelected()}>Submit ranked nomination</Button>
          <h3>Report chart</h3>
          <Input aria-label="Report reason" value={reportReason} onChange={(_, data) => setReportReason(data.value)} placeholder="Reason for report" />
          <Button disabled={!signedIn || busy || !reportReason.trim()} onClick={() => void reportSelected()}>Submit report</Button>
          {actionFeedback && <p role="status">{actionFeedback}</p>}
        </section>
      </div>}
    </div>
  );
}
