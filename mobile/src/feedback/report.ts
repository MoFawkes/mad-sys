import { strToU8, zipSync } from 'fflate';

export const MAX_FILES = 5;
export const MAX_BYTES = 10 * 1024 * 1024;
export const FEEDBACK_CATEGORIES = ['General', 'Timetable', 'Notifications', 'Listening', 'Sign-in', 'Display'] as const;

export type FeedbackDiagnostics = {
  appVersion: string; platform: string; osVersion: string; sessionMode: string;
  timeZone: string; syncStatus: string; lastSyncedAt: string | null;
  selectedClassIds: string[]; timetableCount: number; periodCount: number; hasSyncError: boolean;
};
export type Attachment = { name: string; uri: string; size: number };
export type FeedbackReport = {
  formatVersion: 1; reportId: string; createdAt: string; category: string;
  description: string; expectedBehavior: string; diagnostics?: FeedbackDiagnostics;
  attachments: { name: string; archivePath: string; bytes: number }[];
};

export function archivePath(name: string, index: number): string {
  const basename = name.replace(/\\/g, '/').split('/').pop() ?? '';
  let safe = basename.slice(0, 100).replace(/[^A-Za-z0-9._ -]/g, '_');
  if (!safe.trim() || safe === '.' || safe === '..') safe = 'attachment';
  return `attachments/${String(index + 1).padStart(2, '0')}-${safe}`;
}

export function validateAttachments(files: readonly Attachment[]): void {
  if (files.length > MAX_FILES) throw new Error('Choose up to five attachments.');
  let total = 0;
  for (const file of files) {
    if (!Number.isSafeInteger(file.size) || file.size < 0 || file.size > MAX_BYTES - total)
      throw new Error('Attachments must total 10 MB or less.');
    total += file.size;
  }
}

export function createReport(input: {
  reportId: string; createdAt: string; category: string; description: string; expectedBehavior: string;
  diagnostics?: FeedbackDiagnostics; attachments: readonly Attachment[];
}): FeedbackReport {
  validateAttachments(input.attachments);
  // Explicit fields prevent private picker URIs or future session fields from leaking into the report.
  const details = input.diagnostics;
  return {
    formatVersion: 1, reportId: input.reportId, createdAt: input.createdAt, category: input.category,
    description: input.description.trim(), expectedBehavior: input.expectedBehavior.trim(),
    ...(details ? { diagnostics: {
      appVersion: details.appVersion, platform: details.platform, osVersion: details.osVersion,
      sessionMode: details.sessionMode, timeZone: details.timeZone, syncStatus: details.syncStatus,
      lastSyncedAt: details.lastSyncedAt, selectedClassIds: details.selectedClassIds,
      timetableCount: details.timetableCount, periodCount: details.periodCount, hasSyncError: details.hasSyncError,
    } } : {}),
    attachments: input.attachments.map((file, index) => ({ name: file.name, archivePath: archivePath(file.name, index), bytes: file.size })),
  };
}

export async function createArchive(report: FeedbackReport, files: readonly Attachment[], read: (file: Attachment) => Promise<Uint8Array>): Promise<Uint8Array> {
  validateAttachments(files);
  if (!report.description.trim() || report.description.length > 4000) throw new Error('Describe the problem in up to 4,000 characters.');
  if (report.attachments.length !== files.length) throw new Error('The attachment list changed. Please review it again.');
  const entries: Record<string, Uint8Array> = {
    'report.json': strToU8(JSON.stringify(report, null, 2)),
    'README.txt': strToU8('AQI Clock problem report\nOpen report.json for the description and app details.\nThe attachments folder contains only files selected by the reporter.\nSend this ZIP to the person helping with AQI Clock.\n'),
  };
  let total = 0;
  for (let index = 0; index < files.length; index++) {
    const file = files[index];
    const metadata = report.attachments[index];
    const path = archivePath(file.name, index);
    if (metadata.name !== file.name || metadata.archivePath !== path || metadata.bytes !== file.size)
      throw new Error('The attachment list changed. Please review it again.');
    const bytes = await read(file);
    total += bytes.byteLength;
    if (bytes.byteLength !== file.size || total > MAX_BYTES) throw new Error('An attachment changed. Remove it and choose it again.');
    entries[path] = bytes;
  }
  return zipSync(entries, { level: 0 });
}
