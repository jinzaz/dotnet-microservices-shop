const steps = ['确认订单', '支付订单', '完成'] as const;

export function PaymentProgress({ currentStep }: { currentStep: 1 | 2 | 3 }) {
  return (
    <nav className="payment-progress" aria-label="订单支付进度">
      <ol>
        {steps.map((step, index) => {
          const stepNumber = index + 1;
          const complete = stepNumber < currentStep;
          const current = stepNumber === currentStep;
          return (
            <li
              key={step}
              className={`${complete ? 'is-complete' : ''} ${current ? 'is-current' : ''}`.trim()}
              aria-current={current ? 'step' : undefined}
            >
              <span>{complete ? '✓' : stepNumber}</span>
              <strong>{step}</strong>
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
