import { Redirect, router, useLocalSearchParams } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  Pressable,
  ScrollView,
  StyleSheet,
  Switch,
  Text,
  View,
} from 'react-native';

import {
  CachedClass,
  getClasses,
  getStudentPreferences,
} from '@/src/data/repositories';
import { Field, PrimaryButton } from '@/src/ui/components';
import { useApp } from '@/src/ui/AppProvider';
import { theme } from '@/src/ui/theme';

export default function StudentSetupScreen() {
  const params = useLocalSearchParams<{ code?: string; message?: string }>();
  const { dataRevision, enrollStudent, saveStudentSelection, session } = useApp();
  const [joinCode, setJoinCode] = useState('');
  const [classes, setClasses] = useState<CachedClass[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [optedAm, setOptedAm] = useState(false);
  const [optedPm, setOptedPm] = useState(false);
  const [busy, setBusy] = useState(false);
  const seededCode = useRef(false);

  const isEnrolled =
    session.status === 'signedIn' &&
    session.mode === 'student' &&
    session.studentEnrolled;

  useEffect(() => {
    if (seededCode.current) return;
    seededCode.current = true;
    if (typeof params.code === 'string') setJoinCode(params.code);
  }, [params.code]);

  useEffect(() => {
    if (isEnrolled) {
      void Promise.all([getClasses(), getStudentPreferences()]).then(
        ([availableClasses, preferences]) => {
          setClasses(availableClasses);
          if (preferences) {
            setSelected(new Set(preferences.selectedClassIds));
            setOptedAm(preferences.optedAm);
            setOptedPm(preferences.optedPm);
          }
        },
      );
    }
  }, [dataRevision, isEnrolled]);

  if (session.status === 'loading') {
    return <ActivityIndicator style={styles.loading} color={theme.colors.blue} size="large" />;
  }
  if (session.status === 'signedIn' && session.mode === 'teacher') {
    return <Redirect href="/(tabs)/clock" />;
  }

  const join = async () => {
    if (!joinCode.trim()) {
      Alert.alert('Join code required', 'Enter the code provided by your madrasah.');
      return;
    }
    setBusy(true);
    try {
      await enrollStudent(joinCode);
    } catch (error) {
      Alert.alert(
        'Could not join',
        error instanceof Error ? error.message : 'Check the join code and try again.',
      );
    } finally {
      setBusy(false);
    }
  };

  const save = async () => {
    if (selected.size === 0) {
      Alert.alert('Classes required', 'Select at least one class.');
      return;
    }
    setBusy(true);
    try {
      await saveStudentSelection({
        selectedClassIds: [...selected],
        optedAm,
        optedPm,
      });
      router.replace('/(tabs)/clock');
    } catch (error) {
      Alert.alert('Could not save', error instanceof Error ? error.message : 'Try again.');
    } finally {
      setBusy(false);
    }
  };

  if (!isEnrolled) {
    return (
      <View style={styles.centered}>
        <View style={styles.joinCard}>
          <Text style={styles.joinTitle}>Enter your join code</Text>
          <Text style={styles.joinBody}>
            {typeof params.message === 'string'
              ? params.message
              : 'Ask your teacher for the code.'}
          </Text>
          <Field
            autoCapitalize="characters"
            autoCorrect={false}
            onChangeText={setJoinCode}
            placeholder="XXXX XXXX XXXX XXXX"
            style={styles.joinField}
            value={joinCode}
          />
          <Text style={styles.joinHelp}>16 letters and numbers — spaces don&apos;t matter.</Text>
          <PrimaryButton disabled={busy || !joinCode.trim()} onPress={join}>
            {busy ? 'Joining…' : 'Continue'}
          </PrimaryButton>
        </View>
      </View>
    );
  }

  return (
    <ScrollView contentContainerStyle={styles.content}>
      <Text style={styles.title}>Choose your classes</Text>
      <Text style={styles.body}>You&apos;ll get reminders for these.</Text>

      <View style={styles.options}>
        {classes.map((item) => (
          <CheckRow
            checked={selected.has(item.id)}
            key={item.id}
            label={item.name}
            onPress={() =>
              setSelected((current) => {
                const next = new Set(current);
                if (next.has(item.id)) next.delete(item.id);
                else next.add(item.id);
                return next;
              })
            }
          />
        ))}
      </View>

      <Text style={styles.sectionTitle}>Naseehah (optional)</Text>
      <View style={styles.naseehahCard}>
        <ToggleRow label="Morning (AM)" onValueChange={setOptedAm} value={optedAm} />
        <ToggleRow label="Afternoon (PM)" onValueChange={setOptedPm} value={optedPm} />
      </View>

      <PrimaryButton disabled={busy || classes.length === 0} onPress={save}>
        {busy ? 'Saving…' : 'Start using AQI Clock'}
      </PrimaryButton>
    </ScrollView>
  );
}

function ToggleRow({
  label,
  onValueChange,
  value,
}: {
  label: string;
  onValueChange(value: boolean): void;
  value: boolean;
}) {
  return (
    <View style={styles.toggleRow}>
      <Text style={styles.checkLabel}>{label}</Text>
      <Switch
        accessibilityLabel={label}
        onValueChange={onValueChange}
        thumbColor={theme.colors.cream}
        trackColor={{ false: theme.colors.grey, true: theme.colors.blue }}
        value={value}
      />
    </View>
  );
}

function CheckRow({
  checked,
  label,
  onPress,
}: {
  checked: boolean;
  label: string;
  onPress(): void;
}) {
  return (
    <Pressable
      accessibilityRole="checkbox"
      accessibilityState={{ checked }}
      onPress={onPress}
      style={({ pressed }) => [styles.checkRow, pressed && styles.pressed]}>
      <View style={[styles.checkbox, checked && styles.checkboxChecked]}>
        {checked ? <Text style={styles.tick}>✓</Text> : null}
      </View>
      <Text style={styles.checkLabel}>{label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  body: { color: theme.colors.textOnNavy, fontSize: 17, lineHeight: 24 },
  centered: {
    backgroundColor: theme.colors.navy,
    flex: 1,
    justifyContent: 'center',
    padding: theme.spacing.lg,
  },
  checkbox: {
    alignItems: 'center',
    borderColor: theme.colors.controlBorderOnNavy,
    borderRadius: 5,
    borderWidth: 2,
    height: 25,
    justifyContent: 'center',
    width: 25,
  },
  checkboxChecked: { backgroundColor: theme.colors.blue, borderColor: theme.colors.blue },
  checkLabel: { color: theme.colors.cream, flex: 1, fontSize: 17, fontWeight: '600' },
  checkRow: {
    alignItems: 'center',
    borderColor: theme.colors.softBorderOnNavy,
    borderRadius: 12,
    borderWidth: 1,
    flexDirection: 'row',
    gap: theme.spacing.md,
    minHeight: 54,
    padding: theme.spacing.md,
  },
  content: {
    backgroundColor: theme.colors.navy,
    flexGrow: 1,
    gap: theme.spacing.md,
    padding: theme.spacing.lg,
    paddingBottom: 48,
    paddingTop: 52,
  },
  joinBody: { color: theme.colors.textOnNavy, fontSize: 18, textAlign: 'center' },
  joinCard: {
    backgroundColor: theme.colors.deepNavy,
    borderColor: theme.colors.panelBorderOnNavy,
    borderRadius: 18,
    borderWidth: 1,
    gap: theme.spacing.lg,
    padding: theme.spacing.lg,
  },
  joinField: {
    fontSize: 18,
    letterSpacing: 2,
    textAlign: 'center',
    textTransform: 'uppercase',
  },
  joinHelp: {
    color: theme.colors.mutedTextOnNavy,
    fontSize: 13,
    textAlign: 'center',
  },
  joinTitle: {
    color: theme.colors.cream,
    fontSize: 32,
    fontWeight: '800',
    textAlign: 'center',
  },
  loading: { flex: 1 },
  naseehahCard: {
    borderColor: theme.colors.softBorderOnNavy,
    borderRadius: 12,
    borderWidth: 1,
    overflow: 'hidden',
  },
  options: { gap: theme.spacing.sm },
  pressed: { opacity: 0.75 },
  sectionTitle: {
    color: theme.colors.textOnNavy,
    fontSize: 15,
    fontWeight: '800',
    letterSpacing: 0.8,
    marginTop: theme.spacing.sm,
    textTransform: 'uppercase',
  },
  tick: { color: theme.colors.white, fontSize: 17, fontWeight: '800' },
  title: { color: theme.colors.cream, fontSize: 30, fontWeight: '800' },
  toggleRow: {
    alignItems: 'center',
    borderBottomColor: theme.colors.dividerOnNavy,
    borderBottomWidth: StyleSheet.hairlineWidth,
    flexDirection: 'row',
    minHeight: 66,
    paddingHorizontal: theme.spacing.md,
  },
});
