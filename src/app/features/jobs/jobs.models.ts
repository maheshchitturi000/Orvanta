export interface CandidateProfile {
  fullName: string;
  email: string;
  phone: string;
  headline: string;
  currentTitle: string;
  location: string;
  totalYearsExperience: number;
  skills: string[];
  summary: string;
  linkedInUrl: string;
  portfolioUrl: string;
  cvFileName: string;
  hasCv: boolean;
  updatedAtUtc: string;
}

export interface JobPreferences {
  desiredTitles: string[];
  locations: string[];
  includeRemote: boolean;
  remoteOnly: boolean;
  minMatchScore: number;
  maxPostingAgeDays: number;
  companyBoards: string[];
  excludedCompanies: string[];
  noticePeriodDays: number;
  workAuthorization: string;
  needsVisaSponsorship: boolean;
  willingToRelocate: boolean;
  expectedSalary: string;
  dailyAutomationEnabled: boolean;
  dailyRunHourLocal: number;
  timeZoneId: string;
  lastDiscoveryAtUtc: string | null;
  lastDiscoverySummary: string;
}

export interface JobInfo {
  source: string;
  company: string;
  title: string;
  location: string;
  isRemote: boolean;
  url: string;
  applyUrl: string;
  postedAtUtc: string | null;
}

export type MatchStatus = 'New' | 'Approved' | 'Skipped';

export interface JobMatch {
  id: string;
  score: number;
  status: MatchStatus;
  matchedSkills: string[];
  missingSkills: string[];
  reasons: string[];
  createdAtUtc: string;
  job: JobInfo;
}

export type ApplicationStatus = 'Queued' | 'Preparing' | 'ReadyToSubmit' | 'Submitted' | 'Failed';

export interface JobApplication {
  id: string;
  status: ApplicationStatus;
  channel: string;
  coverLetter: string;
  answers: Record<string, string>;
  error: string;
  approvedAtUtc: string;
  preparedAtUtc: string | null;
  submittedAtUtc: string | null;
  job: JobInfo;
}

export interface JobsSummary {
  hasCv: boolean;
  hasPreferences: boolean;
  newMatches: number;
  queued: number;
  readyToSubmit: number;
  submitted: number;
  failed: number;
  dailyAutomationEnabled: boolean;
  lastDiscoveryAtUtc: string | null;
  lastDiscoverySummary: string;
}

export interface NearMiss {
  score: number;
  company: string;
  title: string;
  location: string;
  source: string;
  url: string;
  reasons: string[];
}

export interface SearchHint {
  message: string;
  /** Route inside the Jobs section, e.g. "profile". */
  link: string | null;
}

export interface DiscoveryResult {
  boardsScanned: number;
  jobsScanned: number;
  newMatches: number;
  errors: string[];
  summary: string;
  nearMisses: NearMiss[];
  hints: SearchHint[];
  rolesUsed: string[];
  searchConnected: boolean;
}

export interface JobSourceStatus {
  name: string;
  displayName: string;
  connected: boolean;
  /** Connected through the server-wide key rather than the user's own. */
  serverKey: boolean;
  connectedAtUtc: string | null;
}

export interface ApproveResult {
  approved: number;
  queued: number;
}
