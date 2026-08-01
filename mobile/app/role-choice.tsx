import { MaterialCommunityIcons } from '@expo/vector-icons';
import { router } from 'expo-router';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import { theme } from '@/src/ui/theme';

export default function RoleChoiceScreen() {
  return (
    <View style={styles.container}>
      <View style={styles.brand}>
        <View style={styles.mark}>
          <MaterialCommunityIcons color={theme.colors.cream} name="book-open-outline" size={48} />
        </View>
        <Text style={styles.title}>AQI Clock</Text>
        <Text style={styles.subtitle}>Madrasah timetable</Text>
      </View>

      <View style={styles.actions}>
        <ChoiceCard
          detail="Sign in with your email"
          label="I'm a teacher"
          onPress={() => router.push('/sign-in')}
        />
        <ChoiceCard
          detail="Choose your classes — no account needed"
          label="I'm a student"
          onPress={() => router.push('/student-setup')}
        />
      </View>
    </View>
  );
}

function ChoiceCard({
  detail,
  label,
  onPress,
}: {
  detail: string;
  label: string;
  onPress(): void;
}) {
  return (
    <Pressable
      accessibilityRole="button"
      onPress={onPress}
      style={({ pressed }) => [styles.choice, pressed && styles.pressed]}>
      <View style={styles.choiceCopy}>
        <Text style={styles.choiceLabel}>{label}</Text>
        <Text style={styles.choiceDetail}>{detail}</Text>
      </View>
      <View style={styles.arrow}>
        <MaterialCommunityIcons color={theme.colors.blue} name="chevron-right" size={34} />
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  actions: { gap: theme.spacing.md, width: '100%' },
  arrow: {
    alignItems: 'center',
    backgroundColor: theme.colors.mutedCream,
    borderRadius: 999,
    height: 58,
    justifyContent: 'center',
    width: 58,
  },
  brand: { alignItems: 'center', gap: 8 },
  choice: {
    alignItems: 'center',
    backgroundColor: theme.colors.cream,
    borderRadius: 18,
    flexDirection: 'row',
    minHeight: 126,
    padding: theme.spacing.lg,
  },
  choiceCopy: { flex: 1, gap: 10, paddingRight: theme.spacing.md },
  choiceDetail: { color: theme.colors.grey, fontSize: 17, lineHeight: 24 },
  choiceLabel: { color: theme.colors.navy, fontSize: 23, fontWeight: '700' },
  container: {
    backgroundColor: theme.colors.navy,
    flex: 1,
    gap: 54,
    justifyContent: 'center',
    padding: theme.spacing.lg,
  },
  mark: {
    alignItems: 'center',
    backgroundColor: theme.colors.deepNavy,
    borderRadius: 999,
    height: 96,
    justifyContent: 'center',
    marginBottom: 18,
    width: 96,
  },
  pressed: { opacity: 0.78, transform: [{ scale: 0.99 }] },
  subtitle: { color: theme.colors.textOnNavy, fontSize: 20 },
  title: { color: theme.colors.cream, fontSize: 34, fontWeight: '800' },
});
