import Constants from 'expo-constants';
import * as DocumentPicker from 'expo-document-picker';
import { File } from 'expo-file-system';
import { router } from 'expo-router';
import { useMemo, useState } from 'react';
import { Platform, Pressable, ScrollView, StyleSheet, Switch, Text, TextInput, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { Attachment, createReport, FEEDBACK_CATEGORIES } from '@/src/feedback/report';
import { shareReport } from '@/src/feedback/shareReport';
import { useApp } from '@/src/ui/AppProvider';
import { PrimaryButton } from '@/src/ui/components';
import { theme } from '@/src/ui/theme';

export default function FeedbackScreen() {
  const { session, sync, snapshot } = useApp();
  const [identity] = useState(() => ({ reportId: `${Date.now()}-${Math.random().toString(36).slice(2, 10)}`, createdAt: new Date().toISOString() }));
  const [category, setCategory] = useState<string>('General');
  const [description, setDescription] = useState('');
  const [expectedBehavior, setExpectedBehavior] = useState('');
  const [includeDetails, setIncludeDetails] = useState(true);
  const [attachments, setAttachments] = useState<Attachment[]>([]);
  const [preview, setPreview] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const report = useMemo(() => createReport({ ...identity, category, description, expectedBehavior, attachments,
    ...(includeDetails ? { diagnostics: {
      appVersion: Constants.expoConfig?.version ?? 'unknown', platform: Platform.OS, osVersion: String(Platform.Version),
      sessionMode: session.status === 'signedIn' ? session.mode : session.status,
      timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone, syncStatus: sync.connectivity,
      lastSyncedAt: sync.lastSyncedAt?.toISOString() ?? null, selectedClassIds: [...(snapshot.viewerClassIds ?? [])].sort(),
      timetableCount: snapshot.timetables.length, periodCount: snapshot.timetables.reduce((count, table) => count + table.periods.length, 0), hasSyncError: Boolean(sync.error),
    } } : {}),
  }), [identity, category, description, expectedBehavior, attachments, includeDetails, session, sync, snapshot]);

  const add = async () => {
    try {
      const result = await DocumentPicker.getDocumentAsync({ multiple: true, copyToCacheDirectory: true, type: '*/*' });
      if (result.canceled) return;
      const next = [...attachments, ...result.assets.map((asset) => ({ name: asset.name, uri: asset.uri, size: new File(asset.uri).size }))];
      createReport({ ...report, attachments: next });
      setAttachments(next); setMessage('');
    } catch (error) { setMessage(error instanceof Error ? error.message : 'Could not attach these files.'); }
  };
  const share = async () => {
    if (!description.trim()) { setMessage('Please describe the problem first.'); return; }
    setBusy(true); setMessage('');
    try {
      await shareReport(report, attachments);
      setMessage('Report prepared. Use the share sheet to send it or save it; closing the sheet does not send a report.');
    } catch (error) { setMessage(error instanceof Error ? error.message : 'Could not prepare the report.'); }
    finally { setBusy(false); }
  };

  return (
    <SafeAreaView edges={['top', 'bottom']} style={styles.screen}>
      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        <Pressable accessibilityRole="button" onPress={() => router.back()}><Text style={styles.link}>‹ Back</Text></Pressable>
        <Text style={styles.title}>Report a problem</Text>
        <Text style={styles.body}>Create a ZIP report to send to the person helping with AQI Clock. Nothing is sent automatically.</Text>
        <Text style={styles.label}>Area</Text>
        <View style={styles.categories}>{FEEDBACK_CATEGORIES.map((item) => (
          <Pressable key={item} accessibilityRole="button" accessibilityState={{ selected: category === item }} onPress={() => setCategory(item)} style={[styles.chip, category === item && styles.selected]}>
            <Text style={styles.body}>{item}</Text>
          </Pressable>
        ))}</View>
        <Text style={styles.label}>What happened?</Text>
        <TextInput accessibilityLabel="What happened?" value={description} onChangeText={setDescription} maxLength={4000} multiline style={styles.input}/>
        <Text style={styles.label}>What did you expect? (optional)</Text>
        <TextInput accessibilityLabel="What did you expect?" value={expectedBehavior} onChangeText={setExpectedBehavior} maxLength={2000} multiline style={styles.input}/>
        <Text style={styles.label}>Attachments (optional)</Text>
        <Text style={styles.body}>Screenshots or files you choose: up to five files, 10 MB total. Files are included as selected.</Text>
        {attachments.map((file, index) => (
          <View style={styles.file} key={`${file.uri}-${index}`}>
            <Text style={[styles.body, styles.fileName]}>{file.name} ({Math.ceil(file.size / 1024)} KB)</Text>
            <Pressable accessibilityRole="button" accessibilityLabel={`Remove ${file.name}`} onPress={() => setAttachments((current) => current.filter((_, i) => i !== index))}><Text style={styles.link}>Remove</Text></Pressable>
          </View>
        ))}
        <Pressable accessibilityRole="button" disabled={busy} onPress={() => void add()} style={styles.chip}><Text style={styles.link}>Add attachments</Text></Pressable>
        <View style={styles.file}><Text style={[styles.label, styles.fileName]}>Include helpful app details</Text><Switch accessibilityLabel="Include helpful app details" value={includeDetails} onValueChange={setIncludeDetails}/></View>
        <Text style={styles.body}>Version, OS, session type, sync status and class selection. Account emails, tokens and raw logs are excluded.</Text>
        <Pressable accessibilityRole="button" onPress={() => setPreview(!preview)}><Text style={styles.link}>{preview ? 'Hide preview' : 'Preview report'}</Text></Pressable>
        {preview && <Text selectable style={styles.preview}>{JSON.stringify(report, null, 2)}</Text>}
        {!!message && <Text accessibilityLiveRegion="polite" style={styles.body}>{message}</Text>}
        <PrimaryButton disabled={busy} onPress={() => void share()}>{busy ? 'Preparing report…' : 'Share report ZIP'}</PrimaryButton>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: theme.colors.navy }, content: { padding: 24, gap: 14, paddingBottom: 40 },
  title: { fontSize: 28, color: theme.colors.cream, fontWeight: '700' }, body: { color: theme.colors.textOnNavy, fontSize: 14, lineHeight: 20 },
  label: { color: theme.colors.cream, fontWeight: '600', marginTop: 8 }, link: { color: theme.colors.linkOnNavy, paddingVertical: 8 },
  input: { color: theme.colors.cream, backgroundColor: theme.colors.raisedNavy, borderRadius: 8, padding: 12, minHeight: 90, textAlignVertical: 'top', borderColor: theme.colors.controlBorderOnNavy, borderWidth: 1 },
  categories: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 }, chip: { borderColor: theme.colors.controlBorderOnNavy, borderWidth: 1, borderRadius: 8, paddingHorizontal: 12, paddingVertical: 8, alignSelf: 'flex-start' },
  selected: { backgroundColor: theme.colors.selectedNavy, borderColor: theme.colors.cream }, file: { flexDirection: 'row', alignItems: 'center', gap: 12 }, fileName: { flex: 1 },
  preview: { color: theme.colors.textOnNavy, backgroundColor: theme.colors.deepNavy, borderRadius: 8, padding: 12, fontSize: 11 },
});
