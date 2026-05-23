'use client';
import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { useLogin } from '@pullapp/features';
import { AuthRepository } from '@pullapp/api-client';
import styles from './login.module.css';

const repository = new AuthRepository();

export default function LoginPage() {
	const router = useRouter();
	const { login, isLoading, error } = useLogin(repository);
	
	const [email,    setEmail]    = useState('');
	const [password, setPassword] = useState('');
	
	async function handleSubmit(e: React.FormEvent) {
		e.preventDefault();
		await login({ email, password });
		router.push('/');
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
				
				{error && <p className={styles.error}>{error}</p>}
				
				<button
					className={styles.button}
					type="submit"
					disabled={isLoading}
				>
					{isLoading ? 'Logowanie…' : 'Zaloguj'}
				</button>
			</form>
		</main>
	);
}
