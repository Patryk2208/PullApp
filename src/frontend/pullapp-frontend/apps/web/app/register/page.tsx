'use client';
import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useRegister } from '@pullapp/features';
import { AuthRepository } from '@pullapp/api-client';
import { isUserOldEnough, isValidEmail } from '@pullapp/domain';
import Link from 'next/link';
import styles from './register.module.css';

// const baseUrl = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000';
const baseUrl = process.env.NEXT_PUBLIC_API_URL ?? '';
const repository = new AuthRepository(baseUrl);

export default function RegisterPage() {
	const router = useRouter();
	const { register, isLoading, error, userId } = useRegister(repository);
	
	const [name,      setName]      = useState('');
	const [surname,   setSurname]   = useState('');
	const [email,     setEmail]     = useState('');
	const [password,  setPassword]  = useState('');
	const [birthDate, setBirthDate] = useState('');
	const [validationError, setValidationError] = useState<string | null>(null);

	async function handleSubmit(e: React.FormEvent) {
		e.preventDefault();
		setValidationError(null);

		if (!name.trim() || !surname.trim() || !email.trim() || !password) {
			setValidationError('Uzupełnij wszystkie pola.');
			return;
		}
		if (!isValidEmail(email)) {
			setValidationError('Nieprawidłowy adres e-mail.');
			return;
		}
		if (password.length < 6) {
			setValidationError('Hasło musi mieć co najmniej 6 znaków.');
			return;
		}
		if (!birthDate) {
			setValidationError('Podaj datę urodzenia.');
			return;
		}
		if (!isUserOldEnough(new Date(birthDate))) {
			setValidationError('Musisz mieć ukończone 18 lat, aby się zarejestrować.');
			return;
		}

		await register({
			name,
			surname,
			email,
			password,
			birthDate,
		});
	}
	
	useEffect(() => {
		if (userId) router.push('/login');
	}, [userId]);
	
	return (
		<main className={styles.container}>
			<h1 className={styles.title}>Rejestracja</h1>
			
			<form className={styles.form} onSubmit={handleSubmit}>
				<label className={styles.label}>
					Imię
					<input className={styles.input} type="text"
					       value={name} onChange={(e) => setName(e.target.value)} />
				</label>
				
				<label className={styles.label}>
					Nazwisko
					<input className={styles.input} type="text"
					       value={surname} onChange={(e) => setSurname(e.target.value)} />
				</label>
				
				<label className={styles.label}>
					E-mail
					<input className={styles.input} type="email"
					       autoComplete="email"
					       value={email} onChange={(e) => setEmail(e.target.value)} />
				</label>
				
				<label className={styles.label}>
					Hasło
					<input className={styles.input} type="password"
					       autoComplete="new-password"
					       value={password} onChange={(e) => setPassword(e.target.value)} />
				</label>
				
				<label className={styles.label}>
					Data urodzenia
					<input className={styles.input} type="date"
					       value={birthDate} onChange={(e) => setBirthDate(e.target.value)}  />
				</label>
				
				{(validationError || error) && (
					<p className={styles.error} data-testid="register-error">{validationError || error}</p>
				)}
				
				<button className={styles.button} type="submit" disabled={isLoading}>
					{isLoading ? 'Rejestrowanie…' : 'Zarejestruj się'}
				</button>
			</form>

			<p style={{ marginTop: '1.25rem', textAlign: 'center', fontSize: '0.9rem', color: '#6b7280' }}>
				Masz już konto?{' '}
				<Link href="/login" data-testid="to-login" style={{ color: '#2563eb', fontWeight: 500 }}>
					Zaloguj się
				</Link>
			</p>
		</main>
	);
}
