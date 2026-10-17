import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiError } from '../../../core/models/response-cls.model';
import { ChipInput } from '../../../shared/ui/chip-input/chip-input';
import { Icon } from '../../../shared/ui/icon/icon';
import { JobsState } from '../jobs-state.service';
import { CandidateProfile, JobPreferences } from '../jobs.models';
import { JobsService } from '../jobs.service';

const ACCEPTED = ['.pdf', '.docx', '.txt'];
const MAX_BYTES = 5 * 1024 * 1024;

/** CV upload, parsed profile (editable) and job search preferences. */
@Component({
  selector: 'app-job-profile',
  imports: [FormsModule, DatePipe, ChipInput, Icon],
  templateUrl: './job-profile.html',
  styleUrl: './job-profile.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class JobProfile implements OnInit {
  private readonly api = inject(JobsService);
  private readonly state = inject(JobsState);

  protected readonly profile = signal<CandidateProfile | null>(null);
  protected readonly preferences = signal<JobPreferences | null>(null);
  protected readonly suggestedBoards = signal<string[]>([]);

  protected readonly uploading = signal(false);
  protected readonly dragging = signal(false);
  protected readonly savingProfile = signal(false);
  protected readonly savingPrefs = signal(false);
  protected readonly message = signal<{ kind: 'success' | 'error'; text: string } | null>(null);

  protected readonly hours = Array.from({ length: 24 }, (_, h) => h);
  protected readonly timeZones = supportedTimeZones();
  protected readonly accept = ACCEPTED.join(',');

  ngOnInit(): void {
    void this.load();
  }

  private async load(): Promise<void> {
    const [profile, prefs, boards] = await Promise.allSettled([
      this.api.profile(),
      this.api.preferences(),
      this.api.suggestedBoards()
    ]);

    if (profile.status === 'fulfilled') this.profile.set(profile.value.statusObject);
    if (prefs.status === 'fulfilled' && prefs.value.statusObject) {
      const p = prefs.value.statusObject;
      // First visit: default to the browser's time zone for the daily run.
      if (!p.lastDiscoveryAtUtc && p.timeZoneId === 'UTC') {
        p.timeZoneId = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
      }
      this.preferences.set(p);
    }
    if (boards.status === 'fulfilled') this.suggestedBoards.set(boards.value.statusObject ?? []);
  }

  // ---------- CV ----------

  protected onFileChosen(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) void this.upload(file);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file) void this.upload(file);
  }

  private async upload(file: File): Promise<void> {
    const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
    if (!ACCEPTED.includes(extension)) {
      this.show('error', 'Upload your CV as a PDF, DOCX or TXT file.');
      return;
    }
    if (file.size > MAX_BYTES) {
      this.show('error', 'CV must be 5 MB or smaller.');
      return;
    }

    this.uploading.set(true);
    this.message.set(null);
    try {
      const response = await this.api.uploadCv(file);
      this.profile.set(response.statusObject);
      this.show('success', response.statusMessage);
      // The server seeds preferences on first upload.
      this.preferences.set((await this.api.preferences()).statusObject);
      void this.state.refresh();
    } catch (error) {
      this.show('error', ApiError.from(error).message);
    } finally {
      this.uploading.set(false);
    }
  }

  // ---------- Profile ----------

  protected patchProfile<K extends keyof CandidateProfile>(key: K, value: CandidateProfile[K]): void {
    this.profile.update(p => (p ? { ...p, [key]: value } : p));
  }

  protected async saveProfile(): Promise<void> {
    const profile = this.profile();
    if (!profile) return;

    this.savingProfile.set(true);
    try {
      const response = await this.api.updateProfile({
        ...profile,
        linkedInUrl: profile.linkedInUrl || undefined,
        portfolioUrl: profile.portfolioUrl || undefined
      } as Partial<CandidateProfile>);
      this.profile.set(response.statusObject);
      this.show('success', response.statusMessage);
    } catch (error) {
      const apiError = ApiError.from(error);
      const first = apiError.fieldErrors ? Object.values(apiError.fieldErrors)[0]?.[0] : null;
      this.show('error', first ?? apiError.message);
    } finally {
      this.savingProfile.set(false);
    }
  }

  // ---------- Preferences ----------

  protected patchPrefs<K extends keyof JobPreferences>(key: K, value: JobPreferences[K]): void {
    this.preferences.update(p => (p ? { ...p, [key]: value } : p));
  }

  protected async savePreferences(): Promise<void> {
    const prefs = this.preferences();
    if (!prefs) return;

    this.savingPrefs.set(true);
    try {
      const response = await this.api.savePreferences(prefs);
      this.preferences.set(response.statusObject);
      this.show('success', response.statusMessage);
      void this.state.refresh();
    } catch (error) {
      const apiError = ApiError.from(error);
      const first = apiError.fieldErrors ? Object.values(apiError.fieldErrors)[0]?.[0] : null;
      this.show('error', first ?? apiError.message);
    } finally {
      this.savingPrefs.set(false);
    }
  }

  protected hourLabel(hour: number): string {
    const suffix = hour < 12 ? 'AM' : 'PM';
    return `${hour % 12 === 0 ? 12 : hour % 12}:00 ${suffix}`;
  }

  private show(kind: 'success' | 'error', text: string): void {
    this.message.set({ kind, text });
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }
}

function supportedTimeZones(): string[] {
  const intl = Intl as unknown as { supportedValuesOf?: (key: string) => string[] };
  const zones = intl.supportedValuesOf?.('timeZone') ?? [];
  return zones.length ? ['UTC', ...zones.filter(z => z !== 'UTC')] : ['UTC', 'Asia/Kolkata', 'Europe/London', 'America/New_York'];
}
