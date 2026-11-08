import { strFromU8, strToU8, unzipSync } from 'fflate';
import { archivePath, createArchive, createReport, FeedbackDiagnostics, MAX_BYTES, validateAttachments } from '@/src/feedback/report';

const base = { reportId: 'test-report', createdAt: '2026-10-26T18:40:00Z', category: 'Timetable', description: 'Lessons repeat', expectedBehavior: 'One row per time' };

describe('shareable feedback reports', () => {
  it('only includes whitelisted app details and omits file picker URIs', () => {
    const diagnostics = { appVersion: '0.16.0-beta.2', platform: 'android', osVersion: '36', sessionMode: 'student', timeZone: 'Europe/London', syncStatus: 'offline', lastSyncedAt: null,
      selectedClassIds: ['class-a'], timetableCount: 2, periodCount: 8, hasSyncError: true, email: 'private@example.test', accessToken: 'secret-token' };
    const report = createReport({ ...base, diagnostics: diagnostics as FeedbackDiagnostics, attachments: [{ name: 'screenshot.png', uri: 'file:///private/cache/screenshot.png', size: 0 }] });
    const json = JSON.stringify(report);
    expect(json).not.toContain('private@example.test');
    expect(json).not.toContain('secret-token');
    expect(json).not.toContain('file:///');
    expect(report.diagnostics?.hasSyncError).toBe(true);
    expect(createReport({ ...base, attachments: [] })).not.toHaveProperty('diagnostics');
  });

  it('creates a readable ZIP with two distinct attachments sharing a filename', async () => {
    const files = [{ name: 'report.json', uri: 'first', size: 3 }, { name: 'report.json', uri: 'second', size: 3 }];
    const report = createReport({ ...base, attachments: files });
    const bytes = await createArchive(report, files, async (file) => strToU8(file.uri === 'first' ? 'one' : 'two'));
    const archive = unzipSync(bytes);
    expect(Object.keys(archive)).toHaveLength(4);
    expect(strFromU8(archive['attachments/01-report.json'])).toBe('one');
    expect(strFromU8(archive['attachments/02-report.json'])).toBe('two');
    expect(JSON.parse(strFromU8(archive['report.json']))).toMatchObject({ description: 'Lessons repeat', formatVersion: 1 });
  });

  it('rejects excessive files, total size and attachment changes', async () => {
    expect(() => validateAttachments(Array.from({ length: 6 }, () => ({ name: 'a', uri: 'a', size: 0 })))).toThrow('five');
    expect(() => validateAttachments([{ name: 'a', uri: 'a', size: MAX_BYTES }, { name: 'b', uri: 'b', size: 1 }])).toThrow('10 MB');
    const files = [{ name: 'a', uri: 'a', size: 1 }];
    await expect(createArchive(createReport({ ...base, attachments: files }), files, async () => strToU8('changed'))).rejects.toThrow('changed');
    expect(archivePath('../../report.json', 0)).toBe('attachments/01-report.json');
    expect(archivePath('..', 1)).toBe('attachments/02-attachment');
  });

  it('does not create a report without a description', async () => {
    await expect(createArchive(createReport({ ...base, description: ' ', attachments: [] }), [], jest.fn())).rejects.toThrow('Describe');
  });
});
