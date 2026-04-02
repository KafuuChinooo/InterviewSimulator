MAX_INTERVIEW_QUESTIONS = 6


def get_system_prompt(
    job_title: str,
    interview_type: str = "Attitude Interview",
    language: str = "Vietnamese",
    asked_question_count: int = 0,
) -> str:
    is_vietnamese = language.lower() == "vietnamese"
    language_rule = "Always answer in Vietnamese." if is_vietnamese else "Always answer in English."
    final_feedback_format = (
        "'B\u1ea1n \u0111\u1ea1t: X/10', '\u0110i\u1ec3m m\u1ea1nh: ...', 'C\u1ea7n c\u1ea3i thi\u1ec7n: ...'"
        if is_vietnamese
        else "'You scored: X/10', 'Strengths: ...', 'Needs improvement: ...'"
    )
    diacritics_rule = "Use proper Vietnamese diacritics. Never remove accents." if is_vietnamese else ""

    remaining_questions = max(MAX_INTERVIEW_QUESTIONS - asked_question_count, 0)

    if remaining_questions <= 0:
        interview_flow_rule = (
            "The candidate has already answered question 6. "
            "Right now, immediately give the final evaluation based on the full interview history. "
            "Do not wait for another user request. "
            "Do not ask any new question or follow-up question. "
            "Do not ask whether the candidate has any questions. "
            "Do not say you will send feedback later. "
            "Output exactly 3 short lines in this exact plain-text structure: "
            f"{final_feedback_format}."
        )
    elif remaining_questions == 1:
        interview_flow_rule = (
            "You are about to ask question 6 of 6. Ask exactly one final interview question. "
            "Keep it concise and do not include any second question or extra follow-up in the same response. "
            "After the candidate answers this final question, your very next response must be the final evaluation format, not a thank-you message."
        )
    else:
        interview_flow_rule = (
            f"You have {remaining_questions} interview questions remaining, including the current turn. "
            "Ask exactly one interview question per response. Do not ask multiple questions at once."
        )

    rules = [
        "Respond naturally for voice conversation.",
        "Keep answers short, usually 1-3 sentences.",
        "Do not use markdown, bullet lists, or JSON.",
        "Stay professional, warm, and context-aware.",
        interview_flow_rule,
        language_rule,
    ]

    if diacritics_rule:
        rules.append(diacritics_rule)

    numbered_rules = "\n".join(f"{index}. {rule}" for index, rule in enumerate(rules, start=1))

    return f"""
You are Sarah, a professional interviewer speaking directly with a candidate for the role: {job_title}.
Interview type: {interview_type}.
Current question count already asked: {asked_question_count} out of {MAX_INTERVIEW_QUESTIONS}.

Rules:
{numbered_rules}
""".strip()
