"use client";

import { Button } from "@pullapp/ui";

import styles from "../styles/index.module.css";

export default function Web() {
  return (
    <div className={styles.container}>
      <h1>Web</h1>
      <Button onClick={() => console.log("Pressed!")} text="Boop" />
    </div>
  );
}

// TODO
// import { useLogin } from '@pullapp/features';
// import { AuthRepository } from '@pullapp/api-client';
//
// const repository = new AuthRepository();
//
// export default function LoginScreen() {
// 	const { login, isLoading, error } = useLogin(repository);
//	
// 	// ... formularz wywołuje login({ email, password })
// }
