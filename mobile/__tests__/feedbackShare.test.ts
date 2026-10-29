import * as Sharing from 'expo-sharing';
import { createReport } from '@/src/feedback/report';
import { shareReport } from '@/src/feedback/shareReport';

const mockCreate = jest.fn();
const mockWrite = jest.fn();
jest.mock('expo-file-system', () => ({ File: jest.fn(() => ({ uri: 'cache/report.zip', create: mockCreate, write: mockWrite })), Paths: { cache: 'cache' } }));
jest.mock('expo-sharing', () => ({ isAvailableAsync: jest.fn(), shareAsync: jest.fn() }));

const report = createReport({ reportId: 'test-report', createdAt: '2026-10-26T18:40:00Z', category: 'General', description: 'A test report', expectedBehavior: '', attachments: [] });

describe('feedback sharing', () => {
  beforeEach(() => jest.clearAllMocks());
  it('opens the system share sheet with a ZIP only after an explicit share action', async () => {
    jest.mocked(Sharing.isAvailableAsync).mockResolvedValue(true);
    jest.mocked(Sharing.shareAsync).mockResolvedValue(undefined);
    expect(Sharing.shareAsync).not.toHaveBeenCalled();
    await shareReport(report, []);
    expect(mockWrite).toHaveBeenCalledWith(expect.any(Uint8Array));
    expect(Sharing.shareAsync).toHaveBeenCalledWith('cache/report.zip', expect.objectContaining({ mimeType: 'application/zip' }));
  });
  it('does not write a file when sharing is unavailable', async () => {
    jest.mocked(Sharing.isAvailableAsync).mockResolvedValue(false);
    await expect(shareReport(report, [])).rejects.toThrow('not available');
    expect(mockCreate).not.toHaveBeenCalled();
  });
});
