import { useState } from 'react';
import { View, Text, StyleSheet, Pressable } from 'react-native';
import { useRouter } from 'expo-router';
import { useLogin } from '@pullapp/features';
import { AuthRepository } from '@pullapp/api-client';
import { Input, FormError } from '@pullapp/ui';

const repository = new AuthRepository();

export default function LoginScreen() {
	const router = useRouter();
	const { login, isLoading, error } = useLogin(repository);
	
	const [email,    setEmail]    = useState('');
	const [password, setPassword] = useState('');
	
	async function handleSubmit() {
		await login({ email, password });
		// jeśli login się powiódł, token jest już w store
		// nawigacja zależy od Twojej struktury routingu
		router.replace('/');
	}
	
	return (
		<View style={styles.container}>
			<Text style={styles.title}>Zaloguj się</Text>
			
			<Input
				label="E-mail"
				value={email}
				onChangeText={setEmail}
				keyboardType="email-address"
				autoCapitalize="none"
			/>
			<Input
				label="Hasło"
				value={password}
				onChangeText={setPassword}
				secureTextEntry
			/>
			
			<FormError message={error} />
			
			<Pressable
				style={[styles.button, isLoading && styles.buttonDisabled]}
				onPress={handleSubmit}
				disabled={isLoading}
			>
				<Text style={styles.buttonText}>
					{isLoading ? 'Logowanie…' : 'Zaloguj'}
				</Text>
			</Pressable>
		</View>
	);
}

const styles = StyleSheet.create({
	container:      { flex: 1, padding: 24, justifyContent: 'center', gap: 16 },
	title:          { fontSize: 28, fontWeight: '600', marginBottom: 8 },
	button:         { backgroundColor: '#111', borderRadius: 8, padding: 14, alignItems: 'center' },
	buttonDisabled: { opacity: 0.5 },
	buttonText:     { color: '#fff', fontSize: 16, fontWeight: '500' },
});