import { MaterialCommunityIcons } from '@expo/vector-icons';
import { router } from 'expo-router';
import { useState } from 'react';
import { Alert, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';

import { getSupabaseClient } from '@/src/data/sessionStore';
import { Field, PrimaryButton } from '@/src/ui/components';
import { useApp } from '@/src/ui/AppProvider';
import { theme } from '@/src/ui/theme';

export default function SignInScreen() {
  const { signIn } = useApp();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);

  async function submit() {
    setBusy(true);
    try {
      await signIn(email, password);
      router.replace('/(tabs)/clock');
    } catch (error) {
      Alert.alert('Could not sign in', error instanceof Error ? error.message : 'Try again.');
    } finally {
      setBusy(false);
    }
  }

  async function resetPassword() {
    if (!email.trim()) {
      Alert.alert('Enter your email', 'Enter your invited account email first.');
      return;
    }
    const { error } = await getSupabaseClient().auth.resetPasswordForEmail(email.trim(), {
      redirectTo: 'aqiclock-mobile://reset-password',
    });
    Alert.alert(
      error ? 'Could not send reset email' : 'Check your email',
      error?.message ?? 'Open the reset link on this phone.',
    );
  }

  return (
    <ScrollView contentContainerStyle={styles.container} keyboardShouldPersistTaps="handled">
      <Pressable accessibilityLabel="Back" onPress={() => router.back()} style={styles.back}>
        <MaterialCommunityIcons color={theme.colors.cream} name="arrow-left" size={30} />
      </Pressable>

      <View style={styles.intro}>
        <View style={styles.avatar}>
          <MaterialCommunityIcons color={theme.colors.blue} name="account-circle-outline" size={54} />
        </View>
        <Text style={styles.title}>Portal Access</Text>
        <Text style={styles.body}>Please authenticate to continue.</Text>
      </View>

      <View style={styles.form}>
        <Text style={styles.label}>Institutional email</Text>
        <Field
          autoComplete="email"
          keyboardType="email-address"
          onChangeText={setEmail}
          placeholder="teacher@madrasah.edu"
          value={email}
        />
        <View style={styles.passwordHeading}>
          <Text style={styles.label}>Password</Text>
          <Pressable onPress={resetPassword}>
            <Text style={styles.link}>Forgot password?</Text>
          </Pressable>
        </View>
        <Field
          autoComplete="current-password"
          onChangeText={setPassword}
          placeholder="Password"
          secureTextEntry
          value={password}
        />
        <PrimaryButton disabled={busy || !email.trim() || !password} onPress={submit}>
          {busy ? 'Signing in…' : 'Sign in'}
        </PrimaryButton>
      </View>

      <Text style={styles.footer}>
        Accounts are created by an administrator.{'\n'}There is no public sign-up.
      </Text>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  avatar: {
    alignItems: 'center',
    backgroundColor: theme.colors.raisedNavy,
    borderRadius: 14,
    height: 84,
    justifyContent: 'center',
    width: 84,
  },
  back: { left: theme.spacing.lg, padding: 4, position: 'absolute', top: 24 },
  body: { color: theme.colors.textOnNavy, fontSize: 18, textAlign: 'center' },
  container: {
    backgroundColor: theme.colors.navy,
    flexGrow: 1,
    gap: 42,
    justifyContent: 'center',
    padding: theme.spacing.lg,
    paddingTop: 74,
  },
  footer: {
    borderTopColor: theme.colors.raisedSurfaceOnNavy,
    borderTopWidth: 1,
    color: theme.colors.faintTextOnNavy,
    fontSize: 14,
    lineHeight: 21,
    paddingTop: theme.spacing.lg,
    textAlign: 'center',
  },
  form: { gap: 14 },
  intro: { alignItems: 'center', gap: 10 },
  label: {
    color: theme.colors.cream,
    fontSize: 15,
    fontWeight: '700',
    letterSpacing: 0.8,
    textTransform: 'uppercase',
  },
  link: { color: theme.colors.blue, fontSize: 15, fontWeight: '600' },
  passwordHeading: {
    alignItems: 'center',
    flexDirection: 'row',
    justifyContent: 'space-between',
    marginTop: 8,
  },
  title: { color: theme.colors.cream, fontSize: 32, fontWeight: '800' },
});
