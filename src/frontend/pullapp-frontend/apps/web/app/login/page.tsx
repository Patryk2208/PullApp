'use client';
import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { useLogin } from '@pullapp/features';
import { AuthRepository } from '@pullapp/api-client';
import { isValidEmail } from '@pullapp/domain';
import Link from 'next/link';
import styles from './login.module.css';

// const baseUrl = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000';
const baseUrl = process.env.NEXT_PUBLIC_API_URL ?? '';
const repository = new AuthRepository(baseUrl);

export default function LoginPage() {
	const router = useRouter();
	const { login, isLoading, error } = useLogin(repository);
	
	const [email,    setEmail]    = useState('');
	const [password, setPassword] = useState('');
	const [validationError, setValidationError] = useState<string | null>(null);

	async function handleSubmit(e: React.FormEvent) {
		e.preventDefault();
		setValidationError(null);

		if (!email.trim() || !password) {
			setValidationError('Podaj e-mail i hasło.');
			return;
		}
		if (!isValidEmail(email)) {
			setValidationError('Nieprawidłowy adres e-mail.');
			return;
		}

		const isSuccess = await login({ email, password });
		if (isSuccess) {
			router.push('/');
		}
	}
	
	return (
		<main className={styles.container}>
			<h1 className={styles.title}>Zaloguj się</h1>
			
			<form className={styles.form} onSubmit={handleSubmit}>
				<label className={styles.label}>
					E-mail
					<input
						className={styles.input}
						type="email"
						value={email}
						onChange={(e) => setEmail(e.target.value)}
						autoComplete="email"
					/>
				</label>
				
				<label className={styles.label}>
					Hasło
					<input
						className={styles.input}
						type="password"
						value={password}
						onChange={(e) => setPassword(e.target.value)}
						autoComplete="current-password"
					/>
				</label>
				
				{(validationError || error) && (
					<p className={styles.error} data-testid="login-error">{validationError || error}</p>
				)}
				
				<button
					className={styles.button}
					type="submit"
					disabled={isLoading}
				>
					{isLoading ? 'Logowanie…' : 'Zaloguj'}
				</button>
			</form>

			<p style={{ marginTop: '1.25rem', textAlign: 'center', fontSize: '0.9rem', color: '#6b7280' }}>
				Nie masz konta?{' '}
				<Link href="/register" data-testid="to-register" style={{ color: '#2563eb', fontWeight: 500 }}>
					Zarejestruj się
				</Link>
			</p>
		</main>
	);
}
