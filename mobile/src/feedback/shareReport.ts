import { File, Paths } from 'expo-file-system';
import * as Sharing from 'expo-sharing';
import { Attachment, createArchive, FeedbackReport } from './report';

export async function shareReport(report: FeedbackReport, attachments: readonly Attachment[]): Promise<void> {
  if (!(await Sharing.isAvailableAsync())) throw new Error('File sharing is not available on this device.');
  const bytes = await createArchive(report, attachments, async (attachment) => {
    const file = new File(attachment.uri);
    if (file.size !== attachment.size) throw new Error('An attachment changed. Remove it and choose it again.');
    return file.bytes();
  });
  const safeId = report.reportId.replace(/[^A-Za-z0-9-]/g, '').slice(0, 64);
  const output = new File(Paths.cache, `aqi-feedback-${safeId || Date.now()}.zip`);
  output.create({ overwrite: true });
  output.write(bytes);
  await Sharing.shareAsync(output.uri, { dialogTitle: 'Share problem report', mimeType: 'application/zip', UTI: 'public.zip-archive' });
}
